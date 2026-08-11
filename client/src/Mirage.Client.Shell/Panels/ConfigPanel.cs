using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mirage.Client.Shell.Input;
using Mirage.Client.Shell.Localization;
using Mirage.Client.Shell.Ui;
using System.Globalization;
using System.Net.Sockets;

namespace Mirage.Client.Shell.Panels;

/// <summary>
/// Pre-connect dialog for editing Server.Host / Server.Port. Test button runs a one-shot
/// TCP probe against a throwaway TcpClient — never touches the real ShellContext.Transport.
/// </summary>
public sealed class ConfigPanel
{
    private readonly DraggablePanel _panel = new(new Rectangle(20, 20, 280, 170), minH: 170, minW: 240);

    public bool IsOpen { get; private set; }
    public Rectangle Bounds => _panel.Bounds;
    public bool ContainsMouse(Point mousePos) => IsOpen && _panel.ContainsMouse(mousePos);
    public string Host => _hostField.Text;
    public int PortValue => int.TryParse(_portField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0;

    private readonly TextInputField _hostField = new() { MaxLength = 128 };
    private readonly TextInputField _portField = new() { MaxLength = 5 };
    private readonly Button _cancelBtn = new();
    private readonly Button _testBtn = new();
    private readonly Button _saveBtn = new();
    private int _labelsGeneration = -1;

    private int _focusedField;
    private int _draggingField = -1;
    private string _status = "";
    private Color _statusColor = Color.LightGray;

    private Task<string?>? _testTask;
    private CancellationTokenSource? _testCts;

    private Rectangle _hostFieldRect;
    private Rectangle _portFieldRect;

    public void Open(string host, int port)
    {
        _hostField.SetText(host ?? "");
        _portField.SetText(port.ToString(CultureInfo.InvariantCulture));
        _status = "";
        _focusedField = 0;
        _draggingField = -1;
        CancelTest();
        IsOpen = true;
    }

    public void Close()
    {
        CancelTest();
        _status = "";
        IsOpen = false;
    }

    public (bool save, bool cancel) Update(InputState input)
    {
        if (!IsOpen) return default;

        _panel.Update(input);
        if (_panel.WasClosed)
        {
            Close();
            return (false, true);
        }

        LayoutControls();
        PollTestTask();

        bool testing = _testTask is not null;
        _testBtn.Enabled = !testing;
        _saveBtn.Enabled = !testing;

        if (input.IsMouseJustPressed())
        {
            bool shift = input.IsKeyDown(Keys.LeftShift) || input.IsKeyDown(Keys.RightShift);
            _draggingField = -1;
            if (_hostFieldRect.Contains(input.MousePosition))
            {
                _focusedField = 0;
                _hostField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 0;
            }
            else if (_portFieldRect.Contains(input.MousePosition))
            {
                _focusedField = 1;
                _portField.HandleMouseClick(input.MousePosition.X, shift);
                _draggingField = 1;
            }
        }
        if (input.IsMouseDown() && !input.IsMouseJustPressed() && _draggingField >= 0)
        {
            if (_draggingField == 0) _hostField.HandleMouseClick(input.MousePosition.X, true);
            else _portField.HandleMouseClick(input.MousePosition.X, true);
        }

        long nowMs = Environment.TickCount64;
        string prevHost = _hostField.Text;
        string prevPort = _portField.Text;
        if (_focusedField == 0) _hostField.Feed(input, nowMs);
        else _portField.Feed(input, nowMs);

        // Digits-only filter on the port field — clamp any pasted/typed non-digit chars.
        string portText = _portField.Text;
        if (portText.Any(c => !char.IsDigit(c)))
            _portField.SetText(new string(portText.Where(char.IsDigit).ToArray()));

        if (input.IsKeyPressed(Keys.Tab))
        {
            _focusedField = 1 - _focusedField;
            input.ConsumeKey(Keys.Tab);
        }

        // Edits invalidate any in-flight test AND any prior success/failure status —
        // a stale "succeeded" against the previously-typed host would mislead.
        if (_hostField.Text != prevHost || _portField.Text != prevPort)
        {
            CancelTest();
            _status = "";
        }

        if (input.IsKeyPressed(Keys.Enter) && !testing)
        {
            input.ConsumeKey(Keys.Enter);
            return TrySave();
        }

        if (_cancelBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            Close();
            return (false, true);
        }
        if (_testBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            StartTest();
        }
        if (_saveBtn.IsClicked(input))
        {
            input.ConsumeMouseClick();
            return TrySave();
        }

        return default;
    }

    public void Draw(SpriteBatch sb, SpriteFont font, InputState input, bool isActive = false)
    {
        if (!IsOpen) return;
        if (_labelsGeneration != ClientStrings.Generation)
        {
            _labelsGeneration = ClientStrings.Generation;
            _cancelBtn.Label = ClientStrings.Get(ClientStrings.Common_Cancel);
            _testBtn.Label = ClientStrings.Get(ClientStrings.ConfigPanel_TestButton);
            _saveBtn.Label = ClientStrings.Get(ClientStrings.ConfigPanel_SaveButton);
        }
        _panel.Draw(sb, font, ClientStrings.Get(ClientStrings.ConfigPanel_Title), isActive);
        LayoutControls();
        long nowMs = Environment.TickCount64;

        var c = _panel.ContentBounds;
        sb.DrawString(font, ClientStrings.Get(ClientStrings.ConfigPanel_HostLabel), new Vector2(c.X + 6, c.Y + 4), UiHelper.DlgLabelColor);
        sb.DrawString(font, ClientStrings.Get(ClientStrings.ConfigPanel_PortLabel), new Vector2(c.X + 6, c.Y + 46), UiHelper.DlgLabelColor);

        _hostField.Draw(sb, font, _hostFieldRect, _focusedField == 0, nowMs);
        _portField.Draw(sb, font, _portFieldRect, _focusedField == 1, nowMs);

        if (_status.Length > 0)
            sb.DrawString(font, _status, new Vector2(c.X + 6, c.Y + 92), _statusColor);

        _cancelBtn.Draw(sb, font, input);
        _testBtn.Draw(sb, font, input, UiHelper.AccentButtonNormal, UiHelper.AccentButtonHover);
        _saveBtn.Draw(sb, font, input, UiHelper.PrimaryButtonNormal, UiHelper.PrimaryButtonHover);

        _panel.DrawOverlay(sb);
    }

    private (bool save, bool cancel) TrySave()
    {
        if (!Validate(out string err))
        {
            _status = err;
            _statusColor = Color.Red;
            return default;
        }
        CancelTest();
        IsOpen = false;
        return (true, false);
    }

    private bool Validate(out string err)
    {
        if (_hostField.Text.Length == 0)
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_HostEmptyError);
            return false;
        }
        if (_portField.Text.Length == 0)
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_PortEmptyError);
            return false;
        }
        if (!int.TryParse(_portField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_PortNotNumberError);
            return false;
        }
        if (p < 1 || p > 65535)
        {
            err = ClientStrings.Get(ClientStrings.ConfigPanel_PortRangeError);
            return false;
        }
        err = "";
        return true;
    }

    private void StartTest()
    {
        if (!Validate(out string err))
        {
            _status = err;
            _statusColor = Color.Red;
            return;
        }
        CancelTest();
        string host = _hostField.Text;
        int port = int.Parse(_portField.Text, CultureInfo.InvariantCulture);
        _status = ClientStrings.Format(ClientStrings.ConfigPanel_TestingConnection, ("Host", host), ("Port", port));
        _statusColor = Color.LightGray;
        _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var token = _testCts.Token;
        string timedOut = ClientStrings.Get(ClientStrings.ConfigPanel_ConnectionTimedOut);
        _testTask = Task.Run<string?>(async () =>
        {
            try
            {
                using var tc = new TcpClient();
                await tc.ConnectAsync(host, port, token);
                return null;
            }
            catch (OperationCanceledException) { return timedOut; }
            catch (Exception ex) { return ex.Message; }
        }, token);
    }

    private void PollTestTask()
    {
        if (_testTask is null || !_testTask.IsCompleted) return;
        string? result;
        if (_testTask.IsFaulted)
            result = _testTask.Exception?.GetBaseException().Message ?? "Unknown error.";
        else
            result = _testTask.Result;
        if (result is null)
        {
            _status = ClientStrings.Get(ClientStrings.ConfigPanel_ConnectionSucceeded);
            _statusColor = Color.LightGreen;
        }
        else
        {
            _status = ClientStrings.Format(ClientStrings.ConfigPanel_ConnectionFailed, ("Error", result));
            _statusColor = Color.Red;
        }
        _testCts?.Dispose();
        _testCts = null;
        _testTask = null;
    }

    private void CancelTest()
    {
        try { _testCts?.Cancel(); } catch { }
        _testCts?.Dispose();
        _testCts = null;
        _testTask = null;
    }

    private void LayoutControls()
    {
        var c = _panel.ContentBounds;
        const int pad = 6;
        const int fieldH = 22;
        _hostFieldRect = new Rectangle(c.X + pad, c.Y + 18, c.Width - pad * 2, fieldH);
        _portFieldRect = new Rectangle(c.X + pad, c.Y + 60, c.Width - pad * 2, fieldH);

        // Buttons split the inner width with two gaps, so they always fit no matter
        // how narrow the panel gets dragged. Right edge stays clear of the resize handle.
        const int btnH = 24, gap = 8, rightReserve = 14;
        int availW = c.Width - pad * 2 - rightReserve;
        int btnW = Math.Max(40, (availW - gap * 2) / 3);
        int totalW = btnW * 3 + gap * 2;
        int startX = c.X + pad + ((availW - totalW) / 2);
        int btnY = c.Bottom - btnH - 8;
        _cancelBtn.Bounds = new Rectangle(startX, btnY, btnW, btnH);
        _testBtn.Bounds = new Rectangle(startX + btnW + gap, btnY, btnW, btnH);
        _saveBtn.Bounds = new Rectangle(startX + (btnW + gap) * 2, btnY, btnW, btnH);
    }
}
