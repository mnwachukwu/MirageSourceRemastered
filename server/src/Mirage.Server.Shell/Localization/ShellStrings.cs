using Mirage.Shared.Localization;

namespace Mirage.Server.Shell.Localization;

/// <summary>
/// Every string the management shell shows, and the runtime accessor for them. Same shape as the
/// editor's <c>EditorStrings</c> and the server's <c>ServerStrings</c>: keys declared with the
/// <c>nameof</c> trick so a const's value always equals its JSON key, and renaming one breaks the
/// lookup on purpose rather than silently returning a placeholder.
///
/// <para><b>There is one language setting, not two.</b> The shell reads the same <c>"Language"</c> from
/// serverconfig.json that the server reads for its console output and logs — this window is the operator's
/// view of that server, so it speaks the operator's language by definition. Per-player messages are
/// unaffected; those resolve off each session's own locale.</para>
/// </summary>
public static class ShellStrings
{
    // ── Keys ──────────────────────────────────────────────────────────────────

    public const string LanguageName = nameof(LanguageName);

    public const string Window_Title = nameof(Window_Title);                     // "{GameName} Server"
    public const string Tab_Server = nameof(Tab_Server);
    public const string Tab_Console = nameof(Tab_Console);

    // ── The server dashboard ──────────────────────────────────────────────────
    public const string Server_Blurb = nameof(Server_Blurb);
    public const string Server_Offline = nameof(Server_Offline);
    public const string Server_World = nameof(Server_World);
    public const string Server_TimeOfDay = nameof(Server_TimeOfDay);
    public const string Server_Weather = nameof(Server_Weather);
    public const string Server_Motd = nameof(Server_Motd);
    public const string Server_MotdHint = nameof(Server_MotdHint);
    public const string Server_Apply = nameof(Server_Apply);
    public const string Server_Uptime = nameof(Server_Uptime);
    public const string Server_Port = nameof(Server_Port);
    public const string Server_Operators = nameof(Server_Operators);
    public const string Server_Players = nameof(Server_Players);
    public const string Server_PlayersEmpty = nameof(Server_PlayersEmpty);
    public const string Server_ColName = nameof(Server_ColName);
    public const string Server_ColAccount = nameof(Server_ColAccount);
    public const string Server_ColLevel = nameof(Server_ColLevel);
    public const string Server_ColClass = nameof(Server_ColClass);
    public const string Server_ColMap = nameof(Server_ColMap);
    public const string Server_ColAccess = nameof(Server_ColAccess);
    public const string Server_Kick = nameof(Server_Kick);
    public const string Server_Mute = nameof(Server_Mute);
    public const string Server_Ban = nameof(Server_Ban);
    public const string Server_Minutes = nameof(Server_Minutes);
    public const string Tab_Configuration = nameof(Tab_Configuration);
    public const string Tab_Commands = nameof(Tab_Commands);

    public const string Commands_Blurb = nameof(Commands_Blurb);
    public const string Commands_Players = nameof(Commands_Players);
    public const string Commands_World = nameof(Commands_World);
    public const string Commands_Guilds = nameof(Commands_Guilds);
    public const string Commands_Run = nameof(Commands_Run);
    public const string Commands_Confirm = nameof(Commands_Confirm);
    public const string Commands_Cancel = nameof(Commands_Cancel);

    public const string Commands_Who = nameof(Commands_Who);
    public const string Commands_Kick = nameof(Commands_Kick);
    public const string Commands_Mute = nameof(Commands_Mute);
    public const string Commands_Ban = nameof(Commands_Ban);
    public const string Commands_SetAccess = nameof(Commands_SetAccess);
    public const string Commands_RefreshBanList = nameof(Commands_RefreshBanList);
    public const string Commands_Tod = nameof(Commands_Tod);
    public const string Commands_Weather = nameof(Commands_Weather);
    public const string Commands_Motd = nameof(Commands_Motd);
    public const string Commands_Respawn = nameof(Commands_Respawn);
    public const string Commands_MapReport = nameof(Commands_MapReport);
    public const string Commands_StartWar = nameof(Commands_StartWar);
    public const string Commands_AdvanceWar = nameof(Commands_AdvanceWar);
    public const string Commands_EndWar = nameof(Commands_EndWar);
    public const string Commands_GuildReset = nameof(Commands_GuildReset);

    public const string Action_Start = nameof(Action_Start);
    public const string Action_Stop = nameof(Action_Stop);
    public const string Action_Send = nameof(Action_Send);
    public const string Action_Save = nameof(Action_Save);
    public const string Action_Revert = nameof(Action_Revert);
    public const string Action_Attach = nameof(Action_Attach);
    public const string Action_Detach = nameof(Action_Detach);
    public const string Action_Generate = nameof(Action_Generate);

    public const string State_Stopped = nameof(State_Stopped);
    public const string State_Running = nameof(State_Running);
    public const string State_Stopping = nameof(State_Stopping);
    public const string State_Detached = nameof(State_Detached);
    public const string State_Attached = nameof(State_Attached);

    public const string Console_CommandHint = nameof(Console_CommandHint);       // placeholder in the command box
    public const string Console_ServerNotFound = nameof(Console_ServerNotFound); // "{Path}"
    public const string Console_StoppingNotice = nameof(Console_StoppingNotice); // "{Seconds}"
    public const string Console_Attaching = nameof(Console_Attaching);           // "{Host}" "{Port}"
    public const string Console_Rejected = nameof(Console_Rejected);
    public const string Console_Unreachable = nameof(Console_Unreachable);       // "{Host}" "{Port}"
    public const string Console_ConnectionLost = nameof(Console_ConnectionLost);
    public const string Console_ShutdownBlocked = nameof(Console_ShutdownBlocked);

    // ── The two halves of the Configuration tab ───────────────────────────────
    public const string Config_WindowGroup = nameof(Config_WindowGroup);
    public const string Config_WindowGroupHint = nameof(Config_WindowGroupHint);
    public const string Config_ServerGroup = nameof(Config_ServerGroup);
    public const string Config_ServerGroupHint = nameof(Config_ServerGroupHint);

    // ── Connection ────────────────────────────────────────────────────────────
    public const string Connection_Heading = nameof(Connection_Heading);
    public const string Connection_Blurb = nameof(Connection_Blurb);
    public const string Connection_Local = nameof(Connection_Local);
    public const string Connection_LocalHint = nameof(Connection_LocalHint);
    public const string Connection_Remote = nameof(Connection_Remote);
    public const string Connection_RemoteHint = nameof(Connection_RemoteHint);
    public const string Connection_Host = nameof(Connection_Host);
    public const string Connection_Port = nameof(Connection_Port);
    public const string Connection_Token = nameof(Connection_Token);
    public const string Connection_TokenHint = nameof(Connection_TokenHint);
    public const string Connection_Reveal = nameof(Connection_Reveal);

    // ── Logging ───────────────────────────────────────────────────────────────
    public const string Logging_Heading = nameof(Logging_Heading);
    public const string Logging_Blurb = nameof(Logging_Blurb);
    public const string Logging_Level = nameof(Logging_Level);
    public const string Logging_LevelHint = nameof(Logging_LevelHint);
    public const string Logging_OutgoingPackets = nameof(Logging_OutgoingPackets);
    public const string Logging_IncomingPackets = nameof(Logging_IncomingPackets);
    public const string Logging_PacketsHint = nameof(Logging_PacketsHint);
    public const string Logging_ServerRetention = nameof(Logging_ServerRetention);
    public const string Logging_NetworkRetention = nameof(Logging_NetworkRetention);
    public const string Logging_RetentionHint = nameof(Logging_RetentionHint);
    public const string Logging_Unavailable = nameof(Logging_Unavailable);

    // ── Remote management, as the SERVER's own setting ────────────────────────
    public const string Management_Heading = nameof(Management_Heading);
    public const string Management_Blurb = nameof(Management_Blurb);
    public const string Management_Enable = nameof(Management_Enable);
    public const string Management_EnableHint = nameof(Management_EnableHint);
    public const string Management_Port = nameof(Management_Port);
    public const string Management_Token = nameof(Management_Token);
    public const string Management_TokenHint = nameof(Management_TokenHint);
    public const string Management_LocalOnly = nameof(Management_LocalOnly);

    public const string Config_DeathPenaltyHeading = nameof(Config_DeathPenaltyHeading);
    public const string Config_DeathPenaltyBlurb = nameof(Config_DeathPenaltyBlurb);
    public const string Config_DurabilityLoss = nameof(Config_DurabilityLoss);
    public const string Config_DurabilityLossHint = nameof(Config_DurabilityLossHint);
    public const string Config_ItemDrop = nameof(Config_ItemDrop);
    public const string Config_ItemDropHint = nameof(Config_ItemDropHint);
    public const string Config_ExpLoss = nameof(Config_ExpLoss);
    public const string Config_ExpLossHint = nameof(Config_ExpLossHint);
    public const string Config_LanguageHeading = nameof(Config_LanguageHeading);
    public const string Config_LanguageBlurb = nameof(Config_LanguageBlurb);
    public const string Config_LanguageServerNote = nameof(Config_LanguageServerNote);
    public const string Config_RestartRequired = nameof(Config_RestartRequired);
    public const string Config_Saved = nameof(Config_Saved);                     // "{Path}"
    public const string Config_SaveFailed = nameof(Config_SaveFailed);           // "{Error}"
    public const string Config_LoadFailed = nameof(Config_LoadFailed);           // "{Error}"

    // ── Runtime accessor ──────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();

    /// <summary>Scans <paramref name="langDir"/> and reads each file's <c>LanguageName</c>, so the
    /// picker lists languages by their own name rather than by locale code — someone choosing Spanish
    /// is looking for "Español", not "es". A malformed file is skipped rather than fatal: one bad
    /// translation must not cost the operator the ability to switch away from it.</summary>
    public static IReadOnlyList<(string Locale, string DisplayName)> GetAvailableLanguages(string langDir)
    {
        var result = new List<(string Locale, string DisplayName)>();
        if (!Directory.Exists(langDir)) return result;
        foreach (string file in Directory.GetFiles(langDir, "*.json"))
        {
            string locale = Path.GetFileNameWithoutExtension(file);
            try
            {
                var dict = StringLoader.Load(file);
                result.Add((locale, dict.TryGetValue(LanguageName, out var n) ? n : locale));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { }
        }
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
        return result;
    }

    /// <summary>Loads <paramref name="langCode"/> from <paramref name="langDir"/>, validating the
    /// translation against en.json. Mismatches throw in DEBUG (a missing key is a bug worth stopping
    /// for while it is cheap to fix) and merge over English in Release (an operator should never be
    /// shown a crash because one label was not translated).</summary>
    public static void Load(string langDir, string langCode = "en")
    {
        var english = StringLoader.Load(Path.Combine(langDir, "en.json"));
        if (langCode == "en") { _current = english; return; }

        var translation = StringLoader.Load(Path.Combine(langDir, $"{langCode}.json"));
        var errors = StringLoader.Validate(english, translation, langCode);
        if (errors.Count > 0)
        {
#if DEBUG
            throw new InvalidOperationException("Translation errors:\n" + string.Join("\n", errors));
#else
            var merged = new Dictionary<string, string>(english);
            foreach (var (k, v) in translation) merged[k] = v;
            _current = merged;
            return;
#endif
        }
        _current = translation;
    }

    public static string Get(string key)
    {
        if (_current.TryGetValue(key, out var v)) return v;
#if DEBUG
        throw new InvalidOperationException($"[ShellStrings] Missing key: \"{key}\"");
#else
        return $"[{key}]";
#endif
    }

    public static string Format(string key, params (string Key, object? Value)[] args)
        => StringLoader.Format(Get(key), args);
}
