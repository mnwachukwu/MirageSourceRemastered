using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using Mirage.Shared;
using Mirage.Shared.Records;

namespace Mirage.Client.Shell.Screens;

/// <summary>Character creation: name, sex, and class, with a live preview of the starting stats and
/// vitals a level-1 character of the chosen class would have.</summary>
public sealed class NewCharScreen : IGameScreen
{
    private readonly ShellContext _ctx;
    private readonly TextInputField _nameField = new() { MaxLength = Constants.NameLength };
    private readonly DropDown _classDropDown = new();
    private readonly List<int> _classNums = [];
    private readonly Button _maleBtn;
    private readonly Button _femaleBtn;
    private readonly Button _createBtn;
    private readonly Button _cancelBtn;
    private Sex _sex = Sex.Male;
    private InputState _input = new();
    // Button captions are captured in the constructor, so a language switch made while this screen
    // is showing would leave them stale — a menu transition rebuilds the screen, but sitting on it
    // does not. Everything else here is fetched inline at draw time and needs no refresh.
    private int _labelsGeneration = -1;

    private void RefreshLabels()
    {
        _maleBtn.Label = ClientStrings.Get(ClientStrings.NewCharScreen_MaleButton);
        _femaleBtn.Label = ClientStrings.Get(ClientStrings.NewCharScreen_FemaleButton);
        _createBtn.Label = ClientStrings.Get(ClientStrings.Common_Create);
        _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
    }
    private string _errorMsg = "";
    private long _lastAnimToggleMs;
    private int _animFrame;

    // frmNewChar coordinates (ScaleMode=3/pixel, ScaleWidth=546, ScaleHeight=304).
    // txtName:    Left=4560=304, Top=1200=80,  Width=3375=225
    // cmbClass:   Left=4560=304, Top=1680=112 (list occupies relative y 112..168)
    // picAddChar: Left=4080=272, Top=3480=232, Width=3000=200, Height=510=34
    // picCancel:  Left=4080=272, Top=3960=264, Width=3000=200, Height=510=34
    // Sex was Visible=0 in original; placed between Name and Class in this implementation.
    // Layout (absolute y): Name 168-194, Sex 200-222, Class 228-250, Stats 276/292, Btns 346/384.
    private static readonly Rectangle Dlg = new(127, 148, 546, 304);
    private static readonly Rectangle NameRect = new(431, 168, 225, 26);  // Dlg + (304, 20)
    private static readonly Rectangle ClassRect = new(431, 228, 225, 22);  // Dlg + (304, 80), header only

    public NewCharScreen(ShellContext ctx)
    {
        _ctx = ctx;
        _maleBtn = new Button { Bounds = new Rectangle(431, 200, 88, 22), Label = ClientStrings.Get(ClientStrings.NewCharScreen_MaleButton) };
        _femaleBtn = new Button { Bounds = new Rectangle(525, 200, 88, 22), Label = ClientStrings.Get(ClientStrings.NewCharScreen_FemaleButton) };
        _createBtn = new Button { Bounds = new Rectangle(399, 396, 96, 34), Label = ClientStrings.Get(ClientStrings.Common_Create) };
        _cancelBtn = new Button { Bounds = new Rectangle(503, 396, 96, 34), Label = ClientStrings.Get(ClientStrings.Common_Cancel) };
    }

    /// <summary>Reset the entry fields and default the class selection, refreshing the stat preview.</summary>
    public void OnEnter()
    {
        _nameField.Clear();
        _errorMsg = "";
        _classDropDown.Items.Clear();
        _classNums.Clear();

        var classes = _ctx.State.Classes;
        if (classes is not null)
        {
            for (int i = 1; i < classes.Length; i++)
            {
                if (classes[i]?.Name.Length > 0)
                {
                    _classDropDown.Items.Add(classes[i].Name);
                    _classNums.Add(i);
                }
            }
        }
        _classDropDown.SelectedIndex = _classDropDown.Items.Count > 0 ? 0 : -1;
    }

    /// <summary>Nothing to release — the screen holds no resources beyond its fields.</summary>
    public void OnExit() { }

    /// <summary>Handle typing, field focus, link clicks, and the submit key; also completes any
    /// in-flight connection attempt started by the submit handler.</summary>
    public void Update(GameTime gameTime, InputState input)
    {
        _input = input;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            RefreshLabels();
        }
        _nameField.Feed(input, Environment.TickCount64);
        _classDropDown.Update(input, ClassRect);

        if (input.IsKeyPressed(Keys.Enter)) TryCreate();
        if (_maleBtn.IsClicked(input)) _sex = Sex.Male;
        if (_femaleBtn.IsClicked(input)) _sex = Sex.Female;
        if (_createBtn.IsClicked(input)) TryCreate();
        if (_cancelBtn.IsClicked(input)) _ctx.Screens.Replace(new CharSelectScreen(_ctx));
    }

    /// <summary>Draw one label/value pair of the starting-stat preview.</summary>
    private static void DrawStat(SpriteBatch sb, SpriteFont font, string label, string value, Color valueColor, float x, float y)
    {
        sb.DrawString(font, label, new Vector2(x, y), Color.DimGray);
        sb.DrawString(font, value, new Vector2(x + font.MeasureString(label).X + 2, y), valueColor);
    }
    /// <summary>Validate the name and selection, then send the create request. The account is already
    /// connected and logged in at this point, so there is no connect step.</summary>
    private void TryCreate()
    {
        if (_nameField.Text.Length < Constants.MinFieldLength)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.NewCharScreen_NameTooShort);
            return;
        }
        if (_classDropDown.SelectedIndex < 0)
        {
            _errorMsg = ClientStrings.Get(ClientStrings.NewCharScreen_SelectClass);
            return;
        }
        _errorMsg = "";
        int classNum = _classNums[_classDropDown.SelectedIndex];
        _ctx.Sender.SendAddChar(_nameField.Text, _sex, classNum);
        _ctx.Menu.GoToLoading(ClientStrings.Get(ClientStrings.NewCharScreen_CreatingCharacter));
        _ctx.Screens.Replace(new LoadingScreen(_ctx));
    }

    /// <summary>Paint the menu dialog, its fields, any error text, and the footer links.</summary>
    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        long now = Environment.TickCount64;
        UiHelper.DrawMenuDialog(sb, _ctx.Graphics.Viewport.Bounds, out _, out _, _ctx.MenuArt);
        UiHelper.DrawMenuTitle(sb, _ctx.TitleFont ?? font, ClientStrings.Get(ClientStrings.NewCharScreen_Title));

        sb.DrawString(font, ClientStrings.Get(ClientStrings.Common_NameLabel), new Vector2(Dlg.X + 216, Dlg.Y + 22), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.NewCharScreen_SexLabel), new Vector2(Dlg.X + 216, Dlg.Y + 54), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.NewCharScreen_ClassLabel), new Vector2(Dlg.X + 216, Dlg.Y + 82), UiHelper.DlgLabelColor);

        _nameField.Draw(sb, font, NameRect, focused: true, now);

        _maleBtn.Enabled = _sex == Sex.Female;
        _femaleBtn.Enabled = _sex == Sex.Male;
        _maleBtn.Draw(sb, font, _input);
        _femaleBtn.Draw(sb, font, _input);
        UiHelper.DrawBorder(sb, _sex == Sex.Male ? _maleBtn.Bounds : _femaleBtn.Bounds, Color.Gold, 2);

        _classDropDown.DrawHeader(sb, font, ClassRect, _input);

        // Stat preview computed for a level-1 character with no base stat points.
        // Formula mirrors StatFormulas: maxHp = (level + str/2 + cls.Str) * 2 at level=1, player str=0.
        var classes = _ctx.State.Classes;
        if (classes is not null && _classDropDown.SelectedIndex >= 0
            && _classDropDown.SelectedIndex < _classNums.Count)
        {
            int idx = _classNums[_classDropDown.SelectedIndex];
            if (idx < classes.Length && classes[idx] is ClassRecord cls)
            {
                long nowMs = Environment.TickCount64;
                if (nowMs - _lastAnimToggleMs >= 250)
                {
                    _animFrame ^= 1;
                    _lastAnimToggleMs = nowMs;
                }
                if (_ctx.Sprites is not null && cls.Sprite >= 0)
                {
                    // Center sprite horizontally in the content area, vertically between stats and Create button.
                    int spriteX = Dlg.X + UiHelper.MenuDlgArtW + (Dlg.Width - UiHelper.MenuDlgArtW - Constants.PicX) / 2;
                    int spriteY = _createBtn.Bounds.Y - Constants.PicY - (_createBtn.Bounds.Y - (Dlg.Y + 204) - Constants.PicY) / 2;
                    bool attacking = _createBtn.IsHovered(_input);
                    int frame = attacking ? (nowMs % 1000L < 500L ? 2 : 0) : _animFrame;
                    UiHelper.DrawMenuSpritePreview(sb, _ctx.Sprites, cls.Sprite, frame,
                        new Rectangle(spriteX, spriteY, Constants.PicX, Constants.PicY));
                }

                // Preview a freshly-created character: Level=1, player stats initialized to class stats.
                int hp = StatFormulas.GetPlayerMaxHp(1, cls.Def, cls.Def);
                int mp = StatFormulas.GetPlayerMaxMp(1, cls.Int, cls.Int);
                int sp = StatFormulas.GetPlayerMaxSp(1, cls.Spd, cls.Spd);
                string crit = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerCriticalChancePerMille(cls.Str, 1));
                string spellCrit = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.SpellCriticalChancePerMille(cls.Int, 1));
                string block = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerBlockChancePerMille(cls.Def, 1));
                string dodge = CombatFormulas.FormatPerMilleAsPercent(CombatFormulas.PlayerDodgeChancePerMille(cls.Def, 1));
                int hpRegen = StatFormulas.GetPlayerHpRegen(cls.Def);
                int mpRegen = StatFormulas.GetPlayerMpRegen(cls.Int);
                int spRegen = StatFormulas.GetPlayerSpRegen(cls.Spd);

                float row1 = Dlg.Y + 126f;
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Str), cls.Str.ToString(), Color.OrangeRed, Dlg.X + 216, row1);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Int), cls.Int.ToString(), Color.DodgerBlue, Dlg.X + 294, row1);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Def), cls.Def.ToString(), Color.LimeGreen, Dlg.X + 372, row1);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Spd), cls.Spd.ToString(), UiHelper.VitalSpColor, Dlg.X + 458, row1);

                float row2 = Dlg.Y + 150f;
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Hp), hp.ToString(), UiHelper.VitalHpColor, Dlg.X + 216, row2);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Mp), mp.ToString(), UiHelper.VitalMpColor, Dlg.X + 326, row2);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Sp), sp.ToString(), UiHelper.VitalSpColor, Dlg.X + 436, row2);

                float row3 = Dlg.Y + 166f;
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_HpRegen), ClientStrings.Format(ClientStrings.Stats_RegenFormat, ("Value", hpRegen)), Color.IndianRed, Dlg.X + 216, row3);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_MpRegen), ClientStrings.Format(ClientStrings.Stats_RegenFormat, ("Value", mpRegen)), Color.SkyBlue, Dlg.X + 326, row3);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_SpRegen), ClientStrings.Format(ClientStrings.Stats_RegenFormat, ("Value", spRegen)), UiHelper.VitalSpColor, Dlg.X + 436, row3);

                float row4 = Dlg.Y + 190f;
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.NewCharScreen_CritLabel), crit, Color.White, Dlg.X + 216, row4);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.NewCharScreen_SpellCritLabel), spellCrit, Color.White, Dlg.X + 280, row4);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Block), block, Color.White, Dlg.X + 406, row4);
                DrawStat(sb, font, ClientStrings.Get(ClientStrings.Stats_Dodge), dodge, Color.White, Dlg.X + 474, row4);
            }
        }

        if (_errorMsg.Length > 0)
            UiHelper.DrawMenuAlert(sb, font, _errorMsg, Color.Red);

        _createBtn.Draw(sb, font, _input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);
        _cancelBtn.Draw(sb, font, _input);

        // Draw the dropdown popup last so it renders on top of all other controls.
        _classDropDown.DrawPopup(sb, font, ClassRect, _input);
    }
}
