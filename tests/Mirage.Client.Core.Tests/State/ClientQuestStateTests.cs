using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Records;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Mirage.Client.Core.Tests;

/// <summary>Client-side quest derivation: the overhead ?/! glyph + the interaction menu are
/// computed from the quest DEFS (SendQuests) + the per-player LOG + the server's ELIGIBLE set (QuestLog), with
/// only the trivial active/done/complete parts derived locally. Glyph codes double as render priority:
/// 0 none / 1 gray "?" / 2 gray "!" / 3 yellow "?" / 4 yellow "!" (higher wins when an NPC fills several roles).</summary>
[TestFixture]
public class ClientQuestStateTests
{
    const int Rat = 20, Giver = 7, TurnIn = 9;

    static QuestRecord Kill(int giver, int turnIn = 0, int count = 3)
    {
        var q = new QuestRecord { Name = "Q", GiverNpc = giver, TurnInNpc = turnIn };
        q.Objectives.Add(new Objective { Kind = ObjectiveKind.Kill, Target = Rat, Count = count });
        return q;
    }

    static ClientState WithQuest(QuestRecord def, List<PlayerQuest> log, params int[] eligible)
    {
        var s = new ClientState();
        s.SetQuestDefs(new[] { (1, def) });
        s.SetQuests(log, eligible, Array.Empty<int>());
        return s;
    }

    [Test]
    public void EligibleGiver_YellowQuestion_AndAcceptMenu()
    {
        var s = WithQuest(Kill(Giver), new List<PlayerQuest>(), 1);

        Assert.That(s.NpcQuestGlyph[Giver], Is.EqualTo(ClientState.QuestGlyphYellowQuestion));
        var actions = new List<(int, ClientState.QuestAction)>(s.ActionableQuestsAt(Giver));
        Assert.That(actions, Is.EqualTo(new[] { (1, ClientState.QuestAction.Accept) }));
    }

    [Test]
    public void IneligibleGiver_NotStarted_GrayQuestion()
    {
        var s = WithQuest(Kill(Giver), new List<PlayerQuest>());   // eligible set empty; never started

        Assert.That(s.NpcQuestGlyph[Giver], Is.EqualTo(ClientState.QuestGlyphGrayQuestion));
        Assert.That(s.ActionableQuestsAt(Giver), Is.Empty, "not eligible -> no accept item");
    }

    [Test]
    public void ActiveTurnIn_IncompleteGrayBang_CompleteYellowBang()
    {
        var def = Kill(Giver, TurnIn, count: 2);
        var log = new List<PlayerQuest> { new() { QuestNum = 1, Status = QuestStatus.InProgress, Progress = new List<int> { 1 } } };
        var s = WithQuest(def, log);   // active, 1/2

        Assert.Multiple(() =>
        {
            Assert.That(s.NpcQuestGlyph[TurnIn], Is.EqualTo(ClientState.QuestGlyphGrayBang), "in progress -> gray !");
            Assert.That(s.NpcQuestGlyph[Giver], Is.EqualTo(ClientState.QuestGlyphNone), "the giver is quiet once accepted");
            Assert.That(s.IsQuestReadyToTurnIn(1), Is.False);
        });

        log[0].Progress[0] = 2;   // complete
        s.SetQuests(log, Array.Empty<int>(), Array.Empty<int>());
        Assert.Multiple(() =>
        {
            Assert.That(s.NpcQuestGlyph[TurnIn], Is.EqualTo(ClientState.QuestGlyphYellowBang), "complete -> yellow !");
            Assert.That(s.IsQuestReadyToTurnIn(1), Is.True);
            var actions = new List<(int, ClientState.QuestAction)>(s.ActionableQuestsAt(TurnIn));
            Assert.That(actions, Is.EqualTo(new[] { (1, ClientState.QuestAction.TurnIn) }));
        });
    }

    [Test]
    public void DoneNonRepeatable_NoGlyph()
    {
        var def = Kill(Giver);   // not repeatable
        var log = new List<PlayerQuest> { new() { QuestNum = 1, Status = QuestStatus.Done, Progress = new List<int> { 3 } } };
        var s = WithQuest(def, log);

        Assert.That(s.NpcQuestGlyph[Giver], Is.EqualTo(ClientState.QuestGlyphNone), "a finished one-time quest shows nothing");
    }

    [Test]
    public void Repeatable_YellowFirstTime_BlueOnceCompleted()
    {
        var def = Kill(Giver, TurnIn, count: 1);
        def.Repeatable = true;
        def.Cadence = QuestCadence.Daily;

        // Eligible to accept but NEVER completed → yellow "?" (first time).
        var s = WithQuest(def, new List<PlayerQuest>(), 1);
        Assert.That(s.NpcQuestGlyph[Giver], Is.EqualTo(ClientState.QuestGlyphYellowQuestion), "a first-time repeatable you can accept is yellow ?");

        // Completed once and available again (a Done log entry, still eligible) → blue "?".
        var doneLog = new List<PlayerQuest> { new() { QuestNum = 1, Status = QuestStatus.Done, Progress = new List<int> { 1 } } };
        s.SetQuests(doneLog, new[] { 1 }, Array.Empty<int>());
        Assert.That(s.NpcQuestGlyph[Giver], Is.EqualTo(ClientState.QuestGlyphBlueQuestion), "a completed repeatable that's available again is blue ?");

        // A repeat run ready to turn in → blue "!" at the turn-in NPC.
        var repeatLog = new List<PlayerQuest> { new() { QuestNum = 1, Status = QuestStatus.InProgressRepeat, Progress = new List<int> { 1 } } };
        s.SetQuests(repeatLog, Array.Empty<int>(), Array.Empty<int>());
        Assert.That(s.NpcQuestGlyph[TurnIn], Is.EqualTo(ClientState.QuestGlyphBlueBang), "a repeat run ready to turn in is blue !");
    }

    // A repeatable quest finished this period stays VISIBLE at its giver (so the offer can still be read) but is
    // not eligible. The cooldown set is what tells the panel why the Accept button is grayed — without it every
    // listed requirement reads as met and the gray button has no stated cause.
    [Test]
    public void Repeatable_DoneThisPeriod_ReportsCooldownAndStaysVisible()
    {
        var def = Kill(Giver, TurnIn, count: 1);
        def.Repeatable = true;
        def.Cadence = QuestCadence.Daily;
        var doneLog = new List<PlayerQuest> { new() { QuestNum = 1, Status = QuestStatus.Done, Progress = new List<int> { 1 } } };

        var s = new ClientState();
        s.SetQuestDefs(new[] { (1, def) });
        s.SetQuests(doneLog, Array.Empty<int>(), new[] { 1 });   // done, not eligible, on cooldown

        var visible = new List<(int, ClientState.QuestAction, bool)>(s.VisibleQuestsAt(Giver));
        Assert.Multiple(() =>
        {
            Assert.That(s.IsQuestOnRepeatCooldown(1), Is.True, "the server flagged this period as already used");
            Assert.That(s.IsQuestEligible(1), Is.False);
            Assert.That(visible, Is.EqualTo(new[] { (1, ClientState.QuestAction.Accept, false) }),
                "still offered for viewing, but not acceptable -> the grayed Accept needs the cooldown reason");
        });

        // Once the period rolls the server re-lights it: eligible again, no cooldown line.
        s.SetQuests(doneLog, new[] { 1 }, Array.Empty<int>());
        Assert.Multiple(() =>
        {
            Assert.That(s.IsQuestOnRepeatCooldown(1), Is.False);
            Assert.That(s.IsQuestEligible(1), Is.True);
        });
    }

    [Test]
    public void MultiRole_MostActionableWins()
    {
        // NPC 7 gives quest 1 (eligible → yellow ?) and is the turn-in for quest 2 (ready → yellow !). yellow ! wins.
        var s = new ClientState();
        s.SetQuestDefs(new[] { (1, Kill(7)), (2, Kill(giver: 5, turnIn: 7, count: 1)) });
        var log = new List<PlayerQuest> { new() { QuestNum = 2, Status = QuestStatus.InProgress, Progress = new List<int> { 1 } } };
        s.SetQuests(log, new[] { 1 }, Array.Empty<int>());   // quest 1 eligible; quest 2 active + complete (1/1)

        Assert.That(s.NpcQuestGlyph[7], Is.EqualTo(ClientState.QuestGlyphYellowBang), "ready-to-turn-in beats accept");
    }
}
