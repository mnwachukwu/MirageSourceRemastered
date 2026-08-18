using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Security;
using System.Collections.ObjectModel;

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

    /// <summary>One row of the known-servers picker. Carries its own caption so the address book stays
    /// free of display text.</summary>
    public sealed record ServerChoice(string Label, string Host, int Port);

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

    public ObservableCollection<ServerChoice> KnownServers { get; } = [];

    public string KnownServersLabel => EditorStrings.Get(EditorStrings.ConnectDialog_KnownServers);
    public string ForgetLabel => EditorStrings.Get(EditorStrings.ConnectDialog_Forget);
    public string AddLabel => EditorStrings.Get(EditorStrings.ConnectDialog_Add);
    public string ServerNameLabel => EditorStrings.Get(EditorStrings.ConnectDialog_ServerName);

    /// <summary>What to call this address in the list. Filled from the picked row, and adopted from the
    /// server's own name on a first connect that leaves it blank.</summary>
    [ObservableProperty] private string _serverName = "";

    /// <summary>The picked row. Setting it fills the host and port fields; typing an address that matches
    /// a known one selects it back.</summary>
    public ServerChoice? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (!SetProperty(ref _selectedServer, value) || value is null) return;
            Host = value.Host;
            Port = value.Port;
            ServerName = ServerBookStore.Book.Find(value.Host, value.Port)?.Name ?? "";
            ForgetServerCommand.NotifyCanExecuteChanged();
        }
    }
    private ServerChoice? _selectedServer;

    public ConnectDialogViewModel(EditorConnection conn)
    {
        _conn = conn;
        RefreshServers();
    }

    private void RefreshServers()
    {
        ServerBookStore.Book.Reload();
        KnownServers.Clear();
        foreach (var e in ServerBookStore.Book.All)
            KnownServers.Add(new ServerChoice(
                e.Name.Length > 0 ? $"{e.Name}  ({e.Host}:{e.Port})" : $"{e.Host}:{e.Port}", e.Host, e.Port));
        SyncSelection();
    }

    // Typing an address that is already known selects it and shows its name. Typing an unknown one keeps
    // whatever name was typed, so a name entered before the address is not thrown away.
    private void SyncSelection()
    {
        string key = ServerBook.KeyFor(Host, Port);
        _selectedServer = KnownServers.FirstOrDefault(c => ServerBook.KeyFor(c.Host, c.Port) == key);
        OnPropertyChanged(nameof(SelectedServer));
        if (_selectedServer is not null)
            ServerName = ServerBookStore.Book.Find(Host, Port)?.Name ?? "";
        ForgetServerCommand.NotifyCanExecuteChanged();
    }

    partial void OnHostChanged(string value)
    {
        SyncSelection();
        AddServerCommand.NotifyCanExecuteChanged();
    }

    partial void OnPortChanged(int value) => SyncSelection();

    private bool CanForgetServer() => SelectedServer is not null;

    [RelayCommand(CanExecute = nameof(CanForgetServer))]
    private void ForgetServer()
    {
        if (SelectedServer is not { } gone) return;
        ServerBookStore.Book.Forget(gone.Host, gone.Port);
        ServerName = "";
        RefreshServers();
    }

    private bool CanAddServer() => !string.IsNullOrWhiteSpace(Host);

    /// <summary>Puts the typed address in the list under the typed name, without connecting to it.
    /// Connecting records it too; this is for setting one up ahead of time.</summary>
    [RelayCommand(CanExecute = nameof(CanAddServer))]
    private void AddServer()
    {
        ServerBookStore.Book.Rename(ServerName, Host, Port);
        RefreshServers();
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
                // A name typed here is the operator's, so it wins over the one the server reported.
                ServerBookStore.Book.Rename(ServerName, Host, Port);
                RefreshServers();
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
            var msg = ex is ServerIdentityChangedException
                ? EditorStrings.Format(EditorStrings.ConnectDialog_IdentityChanged, ("Host", Host), ("Port", Port))
                : EditorStrings.Format(EditorStrings.ConnectDialog_ConnectionError, ("Error", ex.Message));
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
