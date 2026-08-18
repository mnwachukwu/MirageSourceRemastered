using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mirage.Editor.Localization;
using Mirage.Editor.Services;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using System.Collections.ObjectModel;

namespace Mirage.Editor.ViewModels;

/// <summary>
/// The account browser — CREATOR only, and the only editor section that edits a person rather than a
/// piece of content.
///
/// <para>Unlike every other section, this one is ONLINE ONLY and never caches: accounts live on the
/// server, they change while nobody is looking, and a stale page would show an operator a level or a
/// location that has since moved. Every page and every record is fetched on demand, and a save waits for
/// the server to read the record back so the form shows what actually landed — the server clamps a level
/// and refuses an unknown map, and typing something it rejects should not leave the screen claiming
/// otherwise.</para>
///
/// <para> <b>No password, ever.</b> The wire has no field for one, so there is nothing here to show or
/// to send back.  <b>No moderation.</b> Kicks, mutes and bans are an operator's job, done from the
/// server window. Guild membership is shown but not editable — the guild's roster cache is kept in step
/// by GuildSystem, and writing the account's copy directly would desync it.</para>
/// </summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    private const int PageSize = 25;

    private readonly EditorConnection _conn;
    private CancellationTokenSource? _inFlight;

    public AccountEditorViewModel(EditorConnection conn)
    {
        _conn = conn;
        BuildAccessFilters();
        // PageText and GuildText resolve their wording on read, so a language switch has to re-raise them
        // or the pager and the guild line stay in whatever language the section was first opened in.
        EditorStrings.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        // The filter options hold RESOLVED captions, so they are rebuilt rather than re-raised; the
        // current selection is carried across by level.
        BuildAccessFilters();
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(GuildText));
        // A status line is a sentence about something that already happened; re-resolving it would be
        // asserting it happened again, so it is cleared instead.
        StatusMessage = "";
    }

    public ObservableCollection<EditorAccountRow> Accounts { get; } = [];
    public ObservableCollection<AccountCharRowViewModel> Chars { get; } = [];

    /// <summary>Every level a Creator can hand out, for the access picker.</summary>
    public static IReadOnlyList<AdminLevel> AccessLevels { get; } = Enum.GetValues<AdminLevel>();

    /// <summary>The access FILTER's options. Each carries its own caption so the "any level" entry needs
    /// neither a sentinel enum member nor a null the template has to special-case.</summary>
    public ObservableCollection<AccessFilterOption> AccessFilters { get; } = [];

    /// <summary>Narrows the list to one access level; the first option is every level. Costs the server a
    /// full scan, so it is a picker rather than something that fires per keystroke.</summary>
    [ObservableProperty] private AccessFilterOption? _selectedAccessFilter;

    private AdminLevel? AccessFilter => SelectedAccessFilter?.Level;

    private void BuildAccessFilters()
    {
        var keep = SelectedAccessFilter?.Level;
        AccessFilters.Clear();
        AccessFilters.Add(new AccessFilterOption(null, EditorStrings.Get(EditorStrings.AccountEditor_AnyAccess)));
        foreach (var level in Enum.GetValues<AdminLevel>())
            AccessFilters.Add(new AccessFilterOption(level, level.ToString()));
        SelectedAccessFilter = AccessFilters.FirstOrDefault(o => o.Level == keep) ?? AccessFilters[0];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private EditorAccountRow? _selectedAccount;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private int _page;
    [ObservableProperty] private int _total;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";

    // The loaded record's own fields, held apart from the row so an edit in progress is not clobbered by
    // a list refresh.
    [ObservableProperty] private string _login = "";
    [ObservableProperty] private AdminLevel _access;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private int _guild;
    [ObservableProperty] private GuildRank _guildRank;

    public bool HasSelection => SelectedAccount is not null;
    public bool IsOffline => !_conn.IsConnected;

    /// <summary>True while the loaded account is the one this editor session signed in as. Its access
    /// picker is disabled: the server refuses a self-change, and demoting yourself would take away the
    /// section that could put it back.</summary>
    public bool IsSelf => Login.Length > 0 && string.Equals(Login, _conn.Login, StringComparison.OrdinalIgnoreCase);
    public bool CanEditAccess => HasSelection && !IsSelf;

    public int PageCount => Total <= 0 ? 1 : (Total + PageSize - 1) / PageSize;
    public string PageText => EditorStrings.Format(EditorStrings.AccountEditor_PageOf,
        ("Page", Page + 1), ("Count", PageCount), ("Total", Total));

    public bool CanPrev => Page > 0;
    public bool CanNext => Page + 1 < PageCount;

    /// <summary>Read-only, and said so: changing a guild has to go through the guild system so the
    /// roster cache stays in step.</summary>
    public string GuildText => Guild <= 0
        ? EditorStrings.Get(EditorStrings.AccountEditor_NoGuild)
        : EditorStrings.Format(EditorStrings.AccountEditor_GuildFormat, ("Guild", Guild), ("Rank", GuildRank));

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>Offline this section has nothing to show — accounts are the server's, not the world
    /// folder's. The view says so rather than presenting an empty list that looks like "no accounts".</summary>
    public void LoadOffline()
    {
        Accounts.Clear();
        Chars.Clear();
        SelectedAccount = null;
        Notify();
    }

    public void LoadOnline() => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_conn.IsConnected) { LoadOffline(); return; }

        // One request at a time: a fast typist can outrun the server, and an older page landing after a
        // newer one would show the wrong rows for the search box's contents.
        _inFlight?.Cancel();
        _inFlight = new CancellationTokenSource();
        var ct = _inFlight.Token;

        IsBusy = true;
        try
        {
            var reply = await _conn.RequestAccountsAsync(SearchText.Trim(), AccessFilter, Page, PageSize, ct);
            if (reply is null || ct.IsCancellationRequested) return;

            Accounts.Clear();
            foreach (var a in reply.Accounts) Accounts.Add(a);
            Total = reply.Total;
            Page = reply.Page;
            StatusMessage = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            Notify();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        Page = 0;                 // a new search starts at the beginning; page 4 of the old one means nothing
        _ = RefreshAsync();
    }

    partial void OnSelectedAccessFilterChanged(AccessFilterOption? value)
    {
        Page = 0;
        _ = RefreshAsync();
    }

    partial void OnSelectedAccountChanged(EditorAccountRow? value)
    {
        if (value is null)
        {
            Chars.Clear();
            Login = "";
            return;
        }
        _ = LoadAccountAsync(value.Login);
    }

    private async Task LoadAccountAsync(string login)
    {
        if (!_conn.IsConnected) return;
        try
        {
            var record = await _conn.RequestAccountAsync(login);
            if (record is null) return;
            Apply(record);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void Apply(EditorAccountPacket record)
    {
        Login = record.Login;
        Access = record.Access;
        IsOnline = record.IsOnline;
        Guild = record.Guild;
        GuildRank = record.GuildRank;

        Chars.Clear();
        foreach (var c in record.Chars) Chars.Add(new AccountCharRowViewModel(c));
        OnPropertyChanged(nameof(GuildText));
        OnPropertyChanged(nameof(IsSelf));
        OnPropertyChanged(nameof(CanEditAccess));
    }

    // ── Saving ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_conn.IsConnected || Login.Length == 0) return;

        IsBusy = true;
        try
        {
            var reply = await _conn.SaveAccountAsync(new EditorSaveAccountPacket
            {
                Login = Login,
                Access = Access,
                Chars = [.. Chars.Select(c => c.ToRow())],
            });

            // The reply is the server's own re-read. Applying it is what makes a clamped level or a
            // refused map visible instead of leaving the form asserting something that did not happen.
            if (reply is not null) Apply(reply);
            StatusMessage = EditorStrings.Get(EditorStrings.AccountEditor_Saved);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (Login.Length > 0) await LoadAccountAsync(Login);
    }

    // ── Paging ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (!CanPrev) return;
        Page--;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanNext) return;
        Page++;
        await RefreshAsync();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(IsOffline));
    }
}

/// <summary>One entry in the access filter: a level, or null for every level, with the caption it shows.
/// A record rather than a bare <see cref="AdminLevel"/> so "any" needs no sentinel member on an enum that
/// also assigns real access.</summary>
public sealed record AccessFilterOption(AdminLevel? Level, string Label);

/// <summary>One editable character row. Name and class are shown but not editable — a rename has to go
/// through the character-name registry, which is a different job from fixing a level or a position.</summary>
public sealed partial class AccountCharRowViewModel : ObservableObject
{
    private readonly int _slot;
    private readonly string _name;
    private readonly int _class;

    public AccountCharRowViewModel(EditorCharRow row)
    {
        _slot = row.Slot;
        _name = row.Name;
        _class = row.Class;
        _level = row.Level;
        _exp = row.Exp;
        _map = row.Map;
        _x = row.X;
        _y = row.Y;
        _str = row.Str;
        _def = row.Def;
        _spd = row.Spd;
        _int = row.Int;
        _points = row.Points;
    }

    public string Name => _name;
    public int Class => _class;

    [ObservableProperty] private int _level;
    [ObservableProperty] private long _exp;
    [ObservableProperty] private int _map;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private int _str;
    [ObservableProperty] private int _def;
    [ObservableProperty] private int _spd;
    [ObservableProperty] private int _int;
    [ObservableProperty] private int _points;

    public EditorCharRow ToRow() => new()
    {
        Slot = _slot,
        Name = _name,
        Class = _class,
        Level = Level,
        Exp = Exp,
        Map = Map,
        X = X,
        Y = Y,
        Str = Str,
        Def = Def,
        Spd = Spd,
        Int = Int,
        Points = Points,
    };
}
