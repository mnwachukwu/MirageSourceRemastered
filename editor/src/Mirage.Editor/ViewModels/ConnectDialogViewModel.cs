using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The connect prompt: host, port, and credentials, plus the authentication round-trip.
/// <para>On success it raises <see cref="ConnectSuccess"/> with the server's data payload and the
/// account's access level, then asks to close. On failure it normally shows the reason inline and
/// stays open, unless <see cref="CloseOnFailure"/> is set — the reconnect flow uses that so the
/// error can be reported by the dialog that owns it.</para>
/// </summary>
public sealed partial class ConnectDialogViewModel : ObservableObject
{
    private readonly EditorConnection _conn;

    [ObservableProperty] private string _host = AppSettings.Current.DefaultServerHost;
    [ObservableProperty] private int _port = AppSettings.Current.DefaultServerPort;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>When true a failed attempt closes the dialog instead of showing the error inline;
    /// the caller reads <see cref="LastError"/>. Used by the reconnect flow.</summary>
    public bool CloseOnFailure { get; init; }
    /// <summary>Failure reason from the last attempt, set only when <see cref="CloseOnFailure"/> is on.</summary>
    public string? LastError { get; private set; }

    public string ConnectTitle => EditorStrings.Format(EditorStrings.ConnectDialog_Header, ("Game", Constants.GameName));
    /// <summary>Raised on a successful login, carrying the server's data payload and the account's access level.</summary>
    public event Action<EditorDataPacket, AdminLevel>? ConnectSuccess;
    /// <summary>Raised when the dialog should close, whether it succeeded or was canceled.</summary>
    public event Action? CloseRequested;

    public ConnectDialogViewModel(EditorConnection conn)
    {
        _conn = conn;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        ErrorMessage = "";
        IsBusy = true;
        try
        {
            var auth = await _conn.ConnectAndAuthAsync(Host, Port, Username, Password);
            if (auth.Success && auth.Data is not null)
            {
                ConnectSuccess?.Invoke(auth.Data, auth.AccessLevel);
                CloseRequested?.Invoke();
            }
            else if (CloseOnFailure)
            {
                LastError = auth.Message;
                CloseRequested?.Invoke();
            }
            else
            {
                ErrorMessage = auth.Message;
            }
        }
        catch (Exception ex)
        {
            var msg = EditorStrings.Format(EditorStrings.ConnectDialog_ConnectionError, ("Error", ex.Message));
            if (CloseOnFailure)
            {
                LastError = msg;
                CloseRequested?.Invoke();
            }
            else
            {
                ErrorMessage = msg;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
