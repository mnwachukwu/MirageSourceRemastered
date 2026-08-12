using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using Mirage.Shared.Records;

namespace Mirage.Client.Core.State;

/// <summary>Quest and conversation state: definitions, progress, eligibility, and the overhead
/// glyphs those resolve to on each NPC.</summary>
public sealed partial class ClientState
{
    // ── Player quests ─────────────────────────────────────────────────────────
    // QuestDefs: definitions cached at join (SendQuestsPacket), like items/npcs. Quests: the per-character log
    // pushed wholesale by QuestLogPacket. _eligibleQuests: the server-authoritative "can accept now" set, so
    // requirements + repeatable relight aren't re-derived here. NpcQuestGlyph: the DERIVED overhead ?/! marker
    // per NPC template, recomputed from the above whenever the defs or log change.

    /// <summary>Quest definitions (1-based); null slot = no such quest.</summary>
    public QuestRecord[] QuestDefs { get; } = new QuestRecord[Constants.MaxQuests + 1];

    /// <summary>NPC template num → overhead quest glyph (0 none / 1 gray "?" / 2 gray "!" / 3 yellow "?" /
    /// 4 yellow "!"; higher wins). Derived, never pushed. Parallel to NpcDefs.</summary>
    public byte[] NpcQuestGlyph { get; } = new byte[Constants.MaxNpcs + 1];

    /// <summary>The player's quest log — one entry per accepted quest (a never-started quest has no entry).
    /// Replaced whole by each QuestLogPacket.</summary>
    public List<PlayerQuest> Quests { get; private set; } = new();

    private HashSet<int> _eligibleQuests = new();
    // Repeatable quests already completed this period — pushed with the eligible set because the period key is
    // server-local. Not an input to any glyph (those already read as gray/blue); it exists so the quest panel can
    // state the reason a still-visible offer can't be accepted yet.
    private HashSet<int> _cooldownQuests = new();

    /// <summary>Bumped whenever the quest log / eligibility changes, so the quest-log panel rebuilds only on a
    /// real change (mirrors <see cref="MailVersion"/>).</summary>
    public int QuestVersion { get; private set; }

    // Overhead glyph codes — also the render priority (higher wins when an NPC fills several roles). Actionable
    // states split by repeatability: a repeatable quest you can accept / turn in shows BLUE, a one-time quest
    // YELLOW. Within a tier, one-time (yellow) outranks repeatable (blue); turn-in (!) outranks accept (?).
    public const byte QuestGlyphNone = 0, QuestGlyphGrayQuestion = 1, QuestGlyphGrayBang = 2,
        QuestGlyphBlueQuestion = 3, QuestGlyphYellowQuestion = 4, QuestGlyphBlueBang = 5, QuestGlyphYellowBang = 6;

    /// <summary>What a player can do with a quest AT a given NPC (drives the interaction menu).</summary>
    public enum QuestAction { Accept, TurnIn }

    /// <summary>Replace the quest definitions from the join-time SendQuests, then refresh the overhead glyphs.</summary>
    public void SetQuestDefs(IEnumerable<(int Num, QuestRecord Def)> defs)
    {
        Array.Clear(QuestDefs, 0, QuestDefs.Length);
        foreach (var (num, def) in defs)
            if (num >= 1 && num < QuestDefs.Length) QuestDefs[num] = def;
        RecomputeNpcQuestGlyphs();
    }

    /// <summary>Replace ONE quest definition (a live editor UpdateQuest broadcast) + refresh the overhead glyphs.
    /// The server re-pushes the eligible set (QuestLog) separately, so a requirement edit relights too.</summary>
    public void SetQuestDef(int num, QuestRecord def)
    {
        if (num >= 1 && num < QuestDefs.Length) QuestDefs[num] = def;
        RecomputeNpcQuestGlyphs();
    }

    /// <summary>Replace the per-player quest log + the server's eligible and repeat-cooldown sets (from a
    /// QuestLogPacket), then refresh the overhead glyphs and bump <see cref="QuestVersion"/>.</summary>
    public void SetQuests(List<PlayerQuest> quests, IEnumerable<int> eligible, IEnumerable<int> cooldown)
    {
        Quests = quests;
        _eligibleQuests = new HashSet<int>(eligible);
        _cooldownQuests = new HashSet<int>(cooldown);
        QuestVersion++;
        RecomputeNpcQuestGlyphs();
    }

    public PlayerQuest? FindQuest(int questNum)
    {
        foreach (var pq in Quests) if (pq.QuestNum == questNum) return pq;
        return null;
    }

    /// <summary>Set by <c>InputProcessor</c> when the melee key aimed at an interactable NPC on the OTHER plane —
    /// refused rather than sent. One-shot: the Shell drains it into a chat refusal and clears it (same hand-off as
    /// <see cref="BankOpen"/>), because Core owns the decision but has no chat of its own.</summary>
    public bool NpcInteractWrongLayer { get; set; }

    /// <summary>Can the player accept this quest right now (server-authoritative eligible set)?</summary>
    public bool IsQuestEligible(int questNum) => _eligibleQuests.Contains(questNum);

    /// <summary>Is this a repeatable quest the player already completed in the current period, so it re-opens only
    /// once that period rolls over? Server-authoritative, like <see cref="IsQuestEligible"/>. Drives the "already
    /// done this day/week/..." line that explains a grayed Accept whose other requirements are all met.</summary>
    public bool IsQuestOnRepeatCooldown(int questNum) => _cooldownQuests.Contains(questNum);

    /// <summary>An active quest whose every objective is complete — ready to turn in.</summary>
    public bool IsQuestReadyToTurnIn(int questNum)
    {
        var pq = FindQuest(questNum);
        if (pq is null || !IsActiveQuestStatus(pq.Status)) return false;
        var def = questNum >= 1 && questNum < QuestDefs.Length ? QuestDefs[questNum] : null;
        if (def is null) return false;
        for (int k = 0; k < def.Objectives.Count; k++)
            if ((k < pq.Progress.Count ? pq.Progress[k] : 0) < def.Objectives[k].Count) return false;
        return true;
    }

    /// <summary>The quests actionable at NPC template <paramref name="npcNum"/> — eligible-to-accept givers and
    /// ready-to-turn-in quests. Drives the interaction (gossip) menu.</summary>
    public IEnumerable<(int QuestNum, QuestAction Action)> ActionableQuestsAt(int npcNum)
    {
        if (npcNum <= 0) yield break;
        for (int q = 1; q < QuestDefs.Length; q++)
        {
            var def = QuestDefs[q];
            if (def is null || def.TrimmedName.Length == 0) continue;
            if (def.GiverNpc == npcNum && IsQuestEligible(q)) yield return (q, QuestAction.Accept);
            if (def.EffectiveTurnInNpc == npcNum && IsQuestReadyToTurnIn(q)) yield return (q, QuestAction.TurnIn);
        }
    }

    /// <summary>Quests VISIBLE at NPC template <paramref name="npcNum"/> for the interaction menu — the same set the
    /// overhead "?"/"!" reflects: givers the player's CLASS can take (the Eligible flag says whether it can be
    /// accepted yet), plus ready-to-turn-in quests. Class-locked quests are excluded. A giver with Eligible=false
    /// opens the quest panel in read-only "here are the requirements" mode. Mirrors QuestSystem.HasVisibleQuestAt.</summary>
    public IEnumerable<(int QuestNum, QuestAction Action, bool Eligible)> VisibleQuestsAt(int npcNum)
    {
        if (npcNum <= 0) yield break;
        int myClass = Me.Class;
        for (int q = 1; q < QuestDefs.Length; q++)
        {
            var def = QuestDefs[q];
            if (def is null || def.TrimmedName.Length == 0) continue;
            if (myClass > 0 && !ClassGate.Allows(def.AllowedClasses, myClass)) continue;   // class-locked → invisible
            var pq = FindQuest(q);
            bool active = pq is not null && IsActiveQuestStatus(pq.Status);
            bool doneForever = pq is { Status: QuestStatus.Done } && !def.Repeatable;
            if (def.GiverNpc == npcNum && !active && !doneForever)                        // offerable (accept or view)
                yield return (q, QuestAction.Accept, IsQuestEligible(q));
            if (def.EffectiveTurnInNpc == npcNum && IsQuestReadyToTurnIn(q))
                yield return (q, QuestAction.TurnIn, true);
        }
    }

    private static bool IsActiveQuestStatus(QuestStatus s) => s is QuestStatus.InProgress or QuestStatus.InProgressRepeat;

    /// <summary>Refresh the derived overhead glyphs. Public so the local player's data load (which sets the class
    /// the glyph class-filter depends on) can relight them independently of a quest push.</summary>
    public void RefreshQuestGlyphs() => RecomputeNpcQuestGlyphs();

    private void RecomputeNpcQuestGlyphs()
    {
        Array.Clear(NpcQuestGlyph, 0, NpcQuestGlyph.Length);
        for (int q = 1; q < QuestDefs.Length; q++)
        {
            var def = QuestDefs[q];
            if (def is null || def.TrimmedName.Length == 0) continue;
            // A quest locked to a DIFFERENT class shows NO glyph at all — the NPC falls through to its other
            // actions. Only filter once the local class is known (0 = not yet loaded; the SendPlayerData refresh
            // relights it). Other unmet requirements (level/stats/prereq) still surface as a gray "?".
            if (Me.Class > 0 && !ClassGate.Allows(def.AllowedClasses, Me.Class)) continue;
            var pq = FindQuest(q);
            bool active = pq is not null && IsActiveQuestStatus(pq.Status);
            bool doneForever = pq is { Status: QuestStatus.Done } && !def.Repeatable;

            // Giver "?" : eligible to accept → yellow, UNLESS it's a repeatable you've completed before → blue
            // ("available again"); offerable-but-blocked (unmet requirements) → gray; nothing while active / done.
            if (def.GiverNpc >= 1 && def.GiverNpc < NpcQuestGlyph.Length)
            {
                bool repeatDone = def.Repeatable && pq is { Status: QuestStatus.Done };
                byte g = IsQuestEligible(q) ? (repeatDone ? QuestGlyphBlueQuestion : QuestGlyphYellowQuestion)
                       : (!active && !doneForever) ? QuestGlyphGrayQuestion
                       : QuestGlyphNone;
                if (g > NpcQuestGlyph[def.GiverNpc]) NpcQuestGlyph[def.GiverNpc] = g;
            }
            // Turn-in "!" : ready → yellow (first run) / blue (a REPEAT run — done before); gray while not ready.
            int turnIn = def.EffectiveTurnInNpc;
            if (active && turnIn >= 1 && turnIn < NpcQuestGlyph.Length)
            {
                bool repeatRun = pq is { Status: QuestStatus.InProgressRepeat };
                byte g = IsQuestReadyToTurnIn(q) ? (repeatRun ? QuestGlyphBlueBang : QuestGlyphYellowBang)
                       : QuestGlyphGrayBang;
                if (g > NpcQuestGlyph[turnIn]) NpcQuestGlyph[turnIn] = g;
            }
        }
    }

    // ── NPC conversations ─────────────────────────────────────────────────────
    // ConvDefs: dialogue-tree definitions cached at join (SendConversationsPacket), like items/npcs/quests — the
    // client walks a tree locally when a conversation opens. _spokenConversations: this character's visited-set
    // (ConversationLogPacket). NpcConvGlyph: the DERIVED overhead "..." marker per NPC, recomputed from the above
    // whenever the defs or the spoken-set change.

    /// <summary>Conversation definitions (1-based); null slot = no such conversation.</summary>
    public ConversationRecord[] ConvDefs { get; } = new ConversationRecord[Constants.MaxConversations + 1];

    /// <summary>NPC template num → overhead conversation glyph (0 none / 1 gray "..." spoken / 2 yellow "..."
    /// unspoken; higher wins so an unspoken conversation outranks a spoken one). Derived, never pushed.</summary>
    public byte[] NpcConvGlyph { get; } = new byte[Constants.MaxNpcs + 1];

    public const byte ConvGlyphNone = 0, ConvGlyphSpoken = 1, ConvGlyphUnspoken = 2;

    private HashSet<int> _spokenConversations = new();

    /// <summary>Replace the conversation definitions from the join-time SendConversations, then refresh glyphs.</summary>
    public void SetConvDefs(IEnumerable<(int Num, ConversationRecord Def)> defs)
    {
        Array.Clear(ConvDefs, 0, ConvDefs.Length);
        foreach (var (num, def) in defs)
            if (num >= 1 && num < ConvDefs.Length) ConvDefs[num] = def;
        RecomputeNpcConvGlyphs();
    }

    /// <summary>Replace ONE conversation definition (a live editor UpdateConversation broadcast) + refresh glyphs.</summary>
    public void SetConvDef(int num, ConversationRecord def)
    {
        if (num >= 1 && num < ConvDefs.Length) ConvDefs[num] = def;
        RecomputeNpcConvGlyphs();
    }

    /// <summary>Replace the character's spoken-conversation set (from a ConversationLogPacket) + refresh glyphs.</summary>
    public void SetConversationsSpoken(IEnumerable<int> spoken)
    {
        _spokenConversations = new HashSet<int>(spoken);
        RecomputeNpcConvGlyphs();
    }

    /// <summary>The conversation attached to NPC template <paramref name="npcNum"/> (SpeakerNpc), or 0 if none —
    /// drives the context-menu "Talk" item. First non-empty match wins (mirrors GameWorld.ConversationForNpc).</summary>
    public int ConversationForNpc(int npcNum)
    {
        if (npcNum <= 0) return 0;
        for (int c = 1; c < ConvDefs.Length; c++)
        {
            var def = ConvDefs[c];
            if (def is not null && def.SpeakerNpc == npcNum && def.TrimmedName.Length > 0) return c;
        }
        return 0;
    }

    private void RecomputeNpcConvGlyphs()
    {
        Array.Clear(NpcConvGlyph, 0, NpcConvGlyph.Length);
        for (int c = 1; c < ConvDefs.Length; c++)
        {
            var def = ConvDefs[c];
            if (def is null || def.TrimmedName.Length == 0) continue;
            int npc = def.SpeakerNpc;
            if (npc < 1 || npc >= NpcConvGlyph.Length) continue;
            byte g = _spokenConversations.Contains(c) ? ConvGlyphSpoken : ConvGlyphUnspoken;
            if (g > NpcConvGlyph[npc]) NpcConvGlyph[npc] = g;   // yellow (unspoken) outranks gray (spoken)
        }
    }

    /// <summary>1-based; index 0 is unused dummy. Sized dynamically from server.</summary>
    public ClassRecord[] Classes { get; set; } = new ClassRecord[1]; // placeholder until server sends

    // MapGroup defs, cached like the other shared defs: filled in bulk at join (SendMapGroups) and
    // refreshed per-group on a live editor save (UpdateMapGroup). The client resolves a map's EFFECTIVE
    // inheritable values against these on demand via the *Of helpers below — the client-side mirror of the
    // server's GameWorld.*Of(mapNum) — instead of the server baking resolved values into each map packet. That
    // is what lets a group edit land live with no map reload or revision bump. Index 0 unused; a null slot means
    // "no such group" and resolves to the map's own raw values / hard defaults.
    public MapGroupRecord?[] MapGroups { get; } = new MapGroupRecord?[Constants.MaxMapGroups + 1];

    /// <summary>The cached MapGroup a map belongs to, or null (group-less map, or the group not yet received).</summary>
    public MapGroupRecord? GroupOf(MapRecord? map)
    {
        int g = map?.MapGroup ?? 0;
        return g > 0 && g <= Constants.MaxMapGroups ? MapGroups[g] : null;
    }

    // Effective inheritable map values — resolve the map's own value over its group's over the hard default via
    // the shared MapGroupResolve, mirroring GameWorld.*Of on the server. Null-map-safe so render/predict sites can
    // pass an unloaded neighbor cell without a guard.
    public MapMoral MoralOf(MapRecord? map) => map is null ? MapMoral.None : MapGroupResolve.Moral(map, GroupOf(map));
    public int MusicOf(MapRecord? map) => map is null ? 0 : MapGroupResolve.Music(map, GroupOf(map));
    public bool IndoorsOf(MapRecord? map) => map is not null && MapGroupResolve.Indoors(map, GroupOf(map));
    public bool AlwaysDarkOf(MapRecord? map) => map is not null && MapGroupResolve.AlwaysDark(map, GroupOf(map));
}
