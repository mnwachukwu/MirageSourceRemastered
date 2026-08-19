using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Mirage.Client.Core.Cache;
using Mirage.Client.Core.Logic;
using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Net;
using Mirage.Client.Shell.Panels;
using Mirage.Client.Shell.Rendering;
using Mirage.Client.Shell.Screens;
using Mirage.Client.Shell.Sound;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mirage.Client.Shell;

/// <summary>Graphics-device setup and asset loading: tilesets, sprite and item sheets, the color
/// key, and the procedurally generated light-halo and box textures.</summary>
public sealed partial class MirageGame : Game
{
    /// <summary>Loads the localized strings, applies window settings, installs the Alt+F4 filter, and
    /// <summary>Absolute path to a numbered music track.</summary>
    private static string MusicPath(int track) => AppPaths.Asset("assets", "music", $"music{track}.ogg");

    /// builds the screen stack and event wiring. Runs before <see cref="LoadContent"/>.</summary>
    protected override void Initialize()
    {
        _langDir = Path.Combine(AppContext.BaseDirectory, "lang");
        ClientStrings.Load(_langDir, _language);
        var languages = ClientStrings.GetAvailableLanguages(_langDir);
        _optionsPanel.SetLanguages(languages, _language);
        // The ENGINE's name at startup — no server has named a world yet. See ClientState.GameName.
        Window.Title = _state.GameName;
        Window.AllowUserResizing = true;
        Window.TextInput += (_, e) => _input.Accumulate(e.Character);
        Window.ClientSizeChanged += OnClientSizeChanged;

        UiHelper.Init(GraphicsDevice);

        _ctx = new ShellContext
        {
            Screens = _screens,
            State = _state,
            Sender = _sender,
            Menu = _menu,
            Transport = _transport,
            Graphics = GraphicsDevice,
            ExitGame = Exit,
            ServerHost = _serverHost,
            ServerPort = _serverPort,
            RememberLogin = _rememberLogin,
            RememberedLogin = _rememberedLogin,
            Dialog = _dialog,
            OptionsPanel = _optionsPanel,
            OnAspectRatioChanged = v => { _maintainAspectRatio = v; SaveConfig(); },
            OnAlwaysShowBarsChanged = v => { _alwaysShowBars = v; },
            OnShowCombatNumbersChanged = v => { _showCombatNumbers = v; },
            OnUseGamepadChanged = v => { _useGamepad = v; _input.UseGamepad = v; SaveConfig(); },
            OnRestoreDefaults = RestoreWindowDefaults,
            SaveSettings = SaveConfig,
            ShowQuitConfirm = () =>
            {
                if (_dialog.IsVisible || _quitConfirm.IsVisible) return;
                _quitConfirmLockedPos = Window.Position;
                Window.AllowUserResizing = false;
                bool inCombat = _state.Me.LastCombatMs > 0
                    && (Environment.TickCount64 - _state.Me.LastCombatMs) < 10_000
                    && !_state.Me.Dead;  // a corpse isn't in combat (no ghost risk) — always show Logout while dead
                _quitConfirm.Show(Exit, inCombat, onLogout: () =>
                {
                    _ctx!.Sender.SendLogoutToCharSelect();
                    _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.CharSelectScreen_Returning));
                    _screens.Replace(new LoadingScreen(_ctx));
                });
            },
            OnPlayMusicChanged = v =>
            {
                _playMusic = v;
                ApplyMusicEnabled();
                SaveConfig();
            },
            OnMusicVolumeChanged = v => { _musicVolume = v; _music.Volume = v / 100f; SaveConfig(); },
            PlayMenuMusic = () =>
            {
                if (_playMusic && _mainMenuMusic > 0)
                {
                    _music.Play(MusicPath(_mainMenuMusic));
                    _currentMusicTrack = _mainMenuMusic;
                }
            },
            // Routed through the one funnel so the in-game options panel also re-syncs the server session.
            OnLanguageChanged = ApplyLanguage,
        };

        try { _music.Volume = _musicVolume / 100f; }
        catch (NoAudioHardwareException) { HandleNoAudio(); }
        _optionsPanel.PlayMusic = _playMusic;
        _optionsPanel.MusicVolume = _musicVolume;

        WireMenuEvents();
        WireGameEvents();

        try { _screens.Push(new MainMenuScreen(_ctx)); }
        catch (NoAudioHardwareException)
        {
            HandleNoAudio();
            _screens.Push(new MainMenuScreen(_ctx));
        }
        base.Initialize();

        // Apply saved window geometry now that the SDL window exists.
        SdlInterop.SetMinimumSize(Window.Handle, RefW, RefH);
        if (_restoreWindowX != int.MinValue)
            Window.Position = new Point(_restoreWindowX, _restoreWindowY);
        if (_restoreWindowWidth != RefW || _restoreWindowHeight != RefH)
        {
            _gfx.PreferredBackBufferWidth = _restoreWindowWidth;
            _gfx.PreferredBackBufferHeight = _restoreWindowHeight;
            _gfx.ApplyChanges();
        }
        if (_savedWindowMaximized)
        {
            SdlInterop.MaximizeWindow(Window.Handle);
            // SDL_MaximizeWindow is synchronous on Windows, but the ClientSizeChanged event
            // isn't fired until the first SDL event pump (which runs after Initialize returns).
            // Read the actual post-maximize size now so the back buffer is correct on frame 1.
            SdlInterop.GetWindowSize(Window.Handle, out int mw, out int mh);
            if (mw > 0 && mh > 0 && (mw != _gfx.PreferredBackBufferWidth || mh != _gfx.PreferredBackBufferHeight))
            {
                _gfx.PreferredBackBufferWidth = mw;
                _gfx.PreferredBackBufferHeight = mh;
                _gfx.ApplyChanges();
            }
        }
        // Capture the OS-assigned position if nothing was saved.
        if (_restoreWindowX == int.MinValue)
        {
            _restoreWindowX = Window.Position.X;
            _restoreWindowY = Window.Position.Y;
        }
        // Seed the debounce with current state so we don't fire immediately.
        _lastTrackedWindowPos = Window.Position;
        _lastTrackedWindowSize = new Point(Window.ClientBounds.Width, Window.ClientBounds.Height);
        _initialized = true;

        // Install SDL event filter so Alt+F4 shows the quit confirm instead of closing immediately.
        // The filter returns 0 (suppress) for SDL_QUIT when the alt modifier is held and we're
        // in gameplay with no dialog open; all other SDL_QUIT events pass through normally.
        _sdlFilter = SdlEventFilterFunc;
        SdlInterop.SetEventFilter(Marshal.GetFunctionPointerForDelegate(_sdlFilter));
    }

    /// <summary>Re-derives the letterbox and notifies input of the new window size after a resize.</summary>
    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        int w = Window.ClientBounds.Width;
        int h = Window.ClientBounds.Height;
        if (w != _gfx.PreferredBackBufferWidth || h != _gfx.PreferredBackBufferHeight)
        {
            _gfx.PreferredBackBufferWidth = w;
            _gfx.PreferredBackBufferHeight = h;
            _gfx.ApplyChanges();
        }
        // Discard stale mouse button state so the first Update() after a resize
        // doesn't report a phantom click from the OS resize interaction.
        _input.NotifyResize();
    }

    /// <summary>Loads fonts, tilesets, sprite and item atlases, and the heat-haze shader, then generates
    /// the procedural light textures (radial halos and the soft-edged safe-zone box).</summary>
    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("fonts/DefaultFont");
        HudPanel.ComputeLinkLayout(_font);  // center the in-game [Options (O)] / [Help (H)] pair for this font
        HudPanel.ComputePregameLinkLayout(_font);  // center the pre-connect [Configure] / [Options] pair
        _gameFont = Content.Load<SpriteFont>("fonts/GameFont");
        _menuFont = Content.Load<SpriteFont>("fonts/MenuFont");
        _titleFont = Content.Load<SpriteFont>("fonts/TitleFont");
        _bubbleFont = Content.Load<SpriteFont>("fonts/BubbleFont");
        _heatEffect = Content.Load<Effect>("shaders/HeatHaze");
        if (_ctx is not null)
        {
            _ctx.MenuFont = _menuFont;
            _ctx.TitleFont = _titleFont;
        }
        _renderTarget = new RenderTarget2D(GraphicsDevice, RefW, RefH);
        // _worldRT is created/resized on demand in EnsureWorldTarget (its size depends on the window).
        _optionsPanel.MaintainAspectRatio = _maintainAspectRatio;
        _optionsPanel.AlwaysShowBars = _alwaysShowBars;
        _optionsPanel.ShowCombatNumbers = _showCombatNumbers;
        _optionsPanel.UseGamepad = _useGamepad;
        _input.UseGamepad = _useGamepad;

        _tilesets = LoadTilesets();
        var tilesetWidths = new int[_tilesets.Length];
        for (int i = 0; i < _tilesets.Length; i++) tilesetWidths[i] = _tilesets[i]?.Width ?? 0;
        TileAtlas.Init(tilesetWidths);
        _sprites = LoadSpriteSheetForCell(Constants.PicX);        // 32x32: players + size-1 NPCs
        _sprites64 = LoadSpriteSheetForCell(Constants.PicX * 2);  // 64x64: size-2 NPCs (null until art added)
        _sprites96 = LoadSpriteSheetForCell(Constants.PicX * 3);  // 96x96: size-3 NPCs (null until art added)
        _items = LoadSingleFromFolder(Constants.ItemsAssetSubfolder, AppPaths.Asset("assets", "graphics", "Items.bmp"));
        _menuArt = LoadTexture(AppPaths.Asset("assets", "graphics", "MenuArt.jpg"));
        if (_ctx is not null)
        {
            _ctx.MenuArt = _menuArt;
            _ctx.Sprites = _sprites;
            _ctx.Sprites64 = _sprites64;
            _ctx.Sprites96 = _sprites96;
            _ctx.Items = _items;
        }

        _lightHaloOuterTex = MakeRadialHaloTex(GraphicsDevice, LightHaloOuterRadius,
            dist => (byte)(LightModel.OuterFalloff(dist) * 255));
        _lightHaloInnerTex = MakeRadialHaloTex(GraphicsDevice, LightHaloInnerRadius,
            dist => (byte)(LightModel.InnerFalloff(dist) * 255)); // Gaussian, sigma ≈ radius/2.5
        _mapLightTex = MakeBoxLightTex(GraphicsDevice, Camera.ViewW, Camera.ViewH, MapAreaBleed);
    }

    /// <summary>Smoothstep easing on [0,1], used for the light halo falloff curves.</summary>
    private static float Smoothstep(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    // Feathered-box light sized to one map cell inflated by a skirt on every side. The interior is a
    // FLAT 1 covering exactly the inner (cell) region; only the outer `skirt` px on each side feathers
    // 1→0. Drawn per safe cell with the interior aligned to the map, so contiguous safe cells' interiors
    // tile edge-to-edge (seamless under max blend) while the skirts spill past the block's outer boundary.
    // Luminance lives in RGB+A (like the halos) so it multiplies/max-blends by color.
    /// <summary>Generates the safe-zone map light: a flat interior with a feathered skirt, so adjacent
    /// safe maps tile without a seam.</summary>
    private static Texture2D MakeBoxLightTex(GraphicsDevice gd, int innerW, int innerH, int skirt)
    {
        int w = innerW + skirt * 2, h = innerH + skirt * 2;
        var tex = new Texture2D(gd, w, h);
        var pixels = new Color[w * h];
        for (int py = 0; py < h; py++)
        {
            float ey = EdgeSkirt(py, h, skirt);
            for (int px = 0; px < w; px++)
            {
                float ex = EdgeSkirt(px, w, skirt);
                byte a = (byte)(ex * ey * 255f);
                pixels[py * w + px] = new Color(a, a, a, a);
            }
        }
        tex.SetData(pixels);
        return tex;
    }

    // 1 across the interior; smoothstep 1→0 over only the outermost `skirt` px on each side.
    /// <summary>Feather weight for one axis of the box light's skirt; 1 inside the flat interior.</summary>
    private static float EdgeSkirt(int i, int size, int skirt)
    {
        float d = Math.Min(i, size - 1 - i); // distance to the nearest edge
        return Smoothstep(d / skirt);
    }

    // White-luminance radial halo: alpha/RGB carry the falloff, tinted to a warm color at draw time.
    /// <summary>Generates a radial light halo of the given radius, with per-distance alpha from
    /// <paramref name="alphaAt"/> (smoothstep for the outer reach, Gaussian for the flame core).</summary>
    private static Texture2D MakeRadialHaloTex(GraphicsDevice gd, int radius, Func<float, byte> alphaAt)
    {
        int size = radius * 2;
        var tex = new Texture2D(gd, size, size);
        var pixels = new Color[size * size];
        float cx = size / 2f, cy = size / 2f;
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                float dx = px - cx, dy = py - cy;
                float dist = MathF.Sqrt(dx * dx + dy * dy) / radius;
                byte a = dist >= 1f ? (byte)0 : alphaAt(dist);
                pixels[py * size + px] = new Color(a, a, a, a);
            }
        }

        tex.SetData(pixels);
        return tex;
    }

    /// <summary>Loads a texture from an absolute path, returning null when the file is missing.</summary>
    private Texture2D? LoadTexture(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Texture2D.FromStream(GraphicsDevice, stream);
        }
        catch { return null; }
    }

    /// <summary>Loads a .bmp and applies the color key, returning null when the file is missing.</summary>
    private Texture2D? LoadBitmap(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var tex = Texture2D.FromStream(GraphicsDevice, stream);
            ApplyColorKey(tex);
            return tex;
        }
        catch { return null; }
    }

    // Scans assets/graphics/tiles/ for numbered tile sheets (0_*.bmp, 1_*.bmp, ...). The leading number
    // in each filename is the stable sheet index, so adding/removing files never reshuffles existing maps.
    /// <summary>Loads every numbered tile sheet into an array indexed by sheet number. Gaps stay null,
    /// so a missing sheet renders as blank rather than shifting every later sheet's index.</summary>
    private Texture2D?[] LoadTilesets()
    {
        string dir = AppPaths.Asset("assets", "graphics", Constants.TilesAssetSubfolder);
        var byIndex = new Dictionary<int, string>();
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir))
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".bmp" && ext != ".png") continue;
                int idx = ParseSheetIndex(Path.GetFileNameWithoutExtension(path));
                if (idx >= 0 && idx < Constants.MaxTilesets) byIndex[idx] = path;
            }
        }
        if (byIndex.Count == 0) return [];
        int max = 0;
        foreach (int k in byIndex.Keys) if (k > max) max = k;
        var sheets = new Texture2D?[max + 1];
        foreach (var kv in byIndex) sheets[kv.Key] = LoadBitmap(kv.Value);
        return sheets;
    }

    // Parses the leading run of digits in a filename as its sheet index; -1 when there is none.
    /// <summary>Reads the leading sheet index off a tile-sheet filename ("3_forest.bmp" → 3);
    /// -1 when the name doesn't start with one.</summary>
    private static int ParseSheetIndex(string fileName)
    {
        int i = 0;
        while (i < fileName.Length && char.IsDigit(fileName[i])) i++;
        return i > 0 && int.TryParse(fileName[..i], out int n) ? n : -1;
    }

    // Single-sheet load for sprites/items: the first image file in assets/graphics/<subfolder>
    // (alphabetical), else the legacy flat path. Multi-file handling is intentionally deferred.
    /// <summary>Loads the single sheet an asset folder is expected to hold (sprites, items), falling back
    /// to a flat file path when the folder is absent.</summary>
    private Texture2D? LoadSingleFromFolder(string subfolder, string legacyFlatPath)
    {
        string dir = AppPaths.Asset("assets", "graphics", subfolder);
        if (Directory.Exists(dir))
        {
            foreach (var p in Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".bmp" || ext == ".png") return LoadBitmap(p);
            }
        }
        return LoadBitmap(legacyFlatPath);
    }

    // Size-keyed character sprite sheet loader for variable-size NPCs: reads the first .bmp/.png in
    // assets/graphics/sprites/{cell}x{cell}/ (color-keyed via LoadBitmap like every other sheet).  The 32x32
    // sheet falls back to the legacy sprites/ folder + flat Sprites.bmp, so players keep loading even before
    // the sheet is relocated into sprites/32x32/.  A missing 64/96 sheet returns null (art not added yet) and
    // those NPCs simply draw no sprite; their bars/name/collision still work.
    /// <summary>Loads the sprite sheet for one NPC footprint size class (32/64/96 px cells).</summary>
    private Texture2D? LoadSpriteSheetForCell(int cell)
    {
        string sizeSub = Path.Combine(Constants.SpritesAssetSubfolder, $"{cell}x{cell}");
        string dir = AppPaths.Asset("assets", "graphics", sizeSub);
        if (Directory.Exists(dir))
        {
            foreach (var p in Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".bmp" || ext == ".png") return LoadBitmap(p);
            }
        }

        return cell == Constants.PicX
            ? LoadSingleFromFolder(Constants.SpritesAssetSubfolder, AppPaths.Asset("assets", "graphics", "Sprites.bmp"))
            : null;
    }

    // Replace the top-left pixel's color with transparent throughout the texture.
    // Color-key convention: the top-left pixel defines the transparent color.
    /// <summary>Makes every pixel matching the top-left pixel fully transparent, the color-key
    /// convention the .bmp art is authored against.</summary>
    private static void ApplyColorKey(Texture2D tex)
    {
        var pixels = new Color[tex.Width * tex.Height];
        tex.GetData(pixels);
        byte kr = pixels[0].R, kg = pixels[0].G, kb = pixels[0].B;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].R == kr && pixels[i].G == kg && pixels[i].B == kb)
                pixels[i] = Color.Transparent;
        }
        tex.SetData(pixels);
    }
}
