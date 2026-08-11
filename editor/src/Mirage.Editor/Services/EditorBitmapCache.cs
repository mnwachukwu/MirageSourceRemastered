using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Mirage.Shared;

namespace Mirage.Editor.Services;

public sealed class EditorBitmapCache
{
    // Tile sheets indexed by sheet number (gaps may be null), loaded from assets/graphics/tiles/.
    public IReadOnlyList<Bitmap?> Tilesets { get; private set; } = [];
    // Display names per sheet (the filename minus its numeric prefix and extension; "" for gaps).
    public IReadOnlyList<string> TilesetNames { get; private set; } = [];
    public Bitmap? Sprites { get; private set; }
    public Bitmap? Items { get; private set; }

    public void Load(string assetsPath)
    {
        (Tilesets, TilesetNames) = LoadTilesets(assetsPath);
        // Character sheets are size-keyed now (sprites/32x32, /64x64, /96x96); the preview uses the 32x32 sheet
        // (the size-1 / player sheet, whose Sprite rows index every size class).
        Sprites = LoadSingleFromFolder(assetsPath, Path.Combine(Constants.SpritesAssetSubfolder, $"{Constants.PicX}x{Constants.PicX}"), "Sprites.bmp");
        Items = LoadSingleFromFolder(assetsPath, Constants.ItemsAssetSubfolder, "Items.bmp");
    }

    /// <summary>Re-scans the asset folders at runtime (the editor's Refresh Assets button).</summary>
    public void Reload(string assetsPath) => Load(assetsPath);

    // Scans assets/graphics/tiles/ for numbered sheets (0_*.bmp, 1_*.bmp, ...). The leading number in
    // each filename is the stable sheet index.
    private static (Bitmap?[] sheets, string[] names) LoadTilesets(string assetsPath)
    {
        string dir = Path.Combine(assetsPath, Constants.TilesAssetSubfolder);
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
        if (byIndex.Count == 0) return ([], []);
        int max = 0;
        foreach (int k in byIndex.Keys) if (k > max) max = k;
        var sheets = new Bitmap?[max + 1];
        var names = new string[max + 1];
        for (int i = 0; i < names.Length; i++) names[i] = "";
        foreach (var kv in byIndex)
        {
            sheets[kv.Key] = TryLoad(kv.Value);
            names[kv.Key] = SheetDisplayName(Path.GetFileNameWithoutExtension(kv.Value));
        }
        return (sheets, names);
    }

    // Single-sheet load for sprites/items: first image file in the subfolder (alphabetical), else the
    // legacy flat file. Multi-file stitching is intentionally not handled yet.
    private static Bitmap? LoadSingleFromFolder(string assetsPath, string subfolder, string legacyFlatName)
    {
        string dir = Path.Combine(assetsPath, subfolder);
        if (Directory.Exists(dir))
        {
            var file = Directory.EnumerateFiles(dir)
                .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".bmp" or ".png")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (file is not null) return TryLoad(file);
        }
        return TryLoad(Path.Combine(assetsPath, legacyFlatName));
    }

    private static int ParseSheetIndex(string fileName)
    {
        int i = 0;
        while (i < fileName.Length && char.IsDigit(fileName[i])) i++;
        return i > 0 && int.TryParse(fileName[..i], out int n) ? n : -1;
    }

    // "0_Tiles" -> "Tiles", "12_dungeon" -> "dungeon": strip the leading digit run and one following
    // separator (_, -, or space). If nothing remains, fall back to the full stem.
    public static string SheetDisplayName(string fileNameNoExt)
    {
        int i = 0;
        while (i < fileNameNoExt.Length && char.IsDigit(fileNameNoExt[i])) i++;
        if (i > 0 && i < fileNameNoExt.Length && fileNameNoExt[i] is '_' or '-' or ' ') i++;
        string name = fileNameNoExt[i..];
        return name.Length > 0 ? name : fileNameNoExt;
    }

    private static Bitmap? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try { return LoadWithColorKey(path) ?? new Bitmap(path); }
        catch { return null; }
    }

    // Loads a BMP file and returns a WriteableBitmap with the top-left pixel made
    // transparent throughout (the standard color-key convention).
    // Returns null for non-24-bit or compressed BMPs; caller falls back to Bitmap(path).
    private static WriteableBitmap? LoadWithColorKey(string path)
    {
        using var fs = File.OpenRead(path);
        using var reader = new BinaryReader(fs);

        // File header (14 bytes)
        if (reader.ReadUInt16() != 0x4D42) return null; // "BM"
        reader.ReadUInt32(); // file size
        reader.ReadUInt32(); // reserved
        uint dataOffset = reader.ReadUInt32();

        // DIB header — we only handle BITMAPINFOHEADER (40 bytes)
        reader.ReadUInt32();              // header size
        int width = reader.ReadInt32();
        int height = reader.ReadInt32();
        reader.ReadUInt16();              // planes
        ushort bpp = reader.ReadUInt16();
        uint compression = reader.ReadUInt32();

        if (bpp != 24 || compression != 0 || width <= 0) return null;

        bool topDown = height < 0;
        int rows = Math.Abs(height);
        int stride = (width * 3 + 3) & ~3; // rows are padded to 4-byte boundary

        fs.Seek(dataOffset, SeekOrigin.Begin);
        var raw = new byte[stride * rows];
        if (fs.Read(raw, 0, raw.Length) != raw.Length) return null;

        // Top-left pixel: BMP without top-down flag stores rows bottom-up,
        // so the first visual row is the LAST row in the file.
        int topRow = topDown ? 0 : rows - 1;
        byte kb = raw[topRow * stride + 0]; // BMP stores BGR
        byte kg = raw[topRow * stride + 1];
        byte kr = raw[topRow * stride + 2];

        var wb = new WriteableBitmap(
            new PixelSize(width, rows),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var fb = wb.Lock();
        unsafe
        {
            byte* dst = (byte*)fb.Address;
            int rowBytes = fb.RowBytes;

            for (int row = 0; row < rows; row++)
            {
                // Flip row order for bottom-up BMPs
                int srcRow = topDown ? row : (rows - 1 - row);

                for (int col = 0; col < width; col++)
                {
                    byte b = raw[srcRow * stride + col * 3 + 0];
                    byte g = raw[srcRow * stride + col * 3 + 1];
                    byte r = raw[srcRow * stride + col * 3 + 2];
                    byte* p = dst + row * rowBytes + col * 4;

                    if (b == kb && g == kg && r == kr)
                    {
                        p[0] = p[1] = p[2] = p[3] = 0; // fully transparent
                    }
                    else
                    {
                        p[0] = b;
                        p[1] = g;
                        p[2] = r;
                        p[3] = 255;
                    }
                }
            }
        }

        return wb;
    }
}
