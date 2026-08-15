using Mirage.Shared.Localization;

namespace Mirage.Server.Shell.Localization;

/// <summary>
/// Every string the management shell shows, and the runtime accessor for them. Same shape as the
/// editor's <c>EditorStrings</c> and the server's <c>ServerStrings</c>: keys declared with the
/// <c>nameof</c> trick so a const's value always equals its JSON key, and renaming one breaks the
/// lookup on purpose rather than silently returning a placeholder.
///
/// <para><b>There is one language setting, not two.</b> The shell reads the same <c>"Language"</c> from
/// appsettings.json that the server reads for its console output and logs — this window is the operator's
/// view of that server, so it speaks the operator's language by definition. Per-player messages are
/// unaffected; those resolve off each session's own locale.</para>
/// </summary>
public static class ShellStrings
{
    // ── Keys ──────────────────────────────────────────────────────────────────

    public const string LanguageName = nameof(LanguageName);

    public const string Window_Title = nameof(Window_Title);                     // "{GameName} Server"
    public const string Tab_Console = nameof(Tab_Console);
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

    public const string State_Stopped = nameof(State_Stopped);
    public const string State_Running = nameof(State_Running);
    public const string State_Stopping = nameof(State_Stopping);

    public const string Console_CommandHint = nameof(Console_CommandHint);       // placeholder in the command box
    public const string Console_ServerNotFound = nameof(Console_ServerNotFound); // "{Path}"
    public const string Console_StoppingNotice = nameof(Console_StoppingNotice); // "{Seconds}"

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
