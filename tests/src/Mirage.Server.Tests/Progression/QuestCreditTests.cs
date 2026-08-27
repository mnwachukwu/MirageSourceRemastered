using Mirage.Server.Core.GameLogic;
using Mirage.Server.Core.Net;
using Mirage.Server.Core.Players;
using Mirage.Server.Core.World;
using Mirage.Shared;
using Mirage.Shared.Protocol;
using Mirage.Shared.Records;
using NUnit.Framework;

namespace Mirage.Server.Tests;

/// <summary>
/// Who a kill counts for on a QUEST, which is a narrower question than who earns EXP from it.
///
/// <para>🔴 EXP divides by damage share, so a token hit earns a token amount and gains nothing by it. An
/// objective tick does NOT divide — it is the same size however little was done for it — so without a floor,
/// one point of damage on a mob somebody else killed is a full tick and tagging is the fastest way to quest.
/// The floor is <see cref="Constants.QuestCreditDamagePercent"/>.</para>
///
/// <para>A party partner earns it alongside whoever qualified, whether or not they landed a blow, reached by
/// the SAME test the partner-kill EXP bonus applies. A support build that heals rather than hits would
/// otherwise advance a quest never.</para>
/// </summary>
[TestFixture]
public class QuestCreditTests
{
    private const int Map = 1;
    private const int Slot = 1;

    private static (CombatSystem Combat, GameWorld World, PlayerManager Pm) NewCombat()
    {
        var world = new GameWorld();
        var pm = new PlayerManager();
        var dispatcher = new NoOpDispatcher();
        var blood = new BloodSystem(world, dispatcher);
        var movement = new MovementSystem(world, pm, dispatcher, blood);
        var combat = new CombatSystem(world, pm, dispatcher, items: null!, movement, joinLeave: null!, blood,
            objectives: new ObjectiveSystem(), guilds: null!, guildWar: null!, territory: null!);
        return (combat, world, pm);
    }

    private static void Register(GameWorld world, PlayerManager pm, int index, int mapNum = Map)
    {
        var sp = pm[index];
        sp.IsConnected = true;
        sp.InGame = true;
        sp.CharNum = 1;
        sp.Char.Name = $"player{index}";
        sp.Char.Map = mapNum;
        world.MapObservers[mapNum].Add(index);
    }

    private static void Party(PlayerManager pm, int a, int b)
    {
        pm[a].InParty = true;
        pm[a].PartyPlayer = b;
        pm[b].InParty = true;
        pm[b].PartyPlayer = a;
    }

    // The damage ledger for one kill, and who the kernel is told about.
    private static HashSet<int> CreditFor(CombatSystem combat, GameWorld world, params (int Index, int Damage)[] hits)
    {
        var mn = world.MapNpcs[Map, Slot];
        mn.Num = 1;
        long total = 0;
        var contributors = new HashSet<int>();
        foreach (var (index, damage) in hits)
        {
            mn.DamageByPlayer[index] = damage;
            total += damage;
            if (damage > 0) contributors.Add(index);
        }

        return combat.QuestCreditFor(mn, Map, total, contributors);
    }

    [Test]
    public void AShareBelowTheFloor_EarnsNothing()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);

        // 11 of 100 — just under.
        var credit = CreditFor(combat, world, (1, 89), (2, 11));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1), "the player who did the work");
            Assert.That(credit, Does.Not.Contain(2), "and not the one who tagged it");
        });
    }

    [Test]
    public void AShareAtTheFloor_Earns()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);

        var credit = CreditFor(combat, world, (1, 88), (2, Constants.QuestCreditDamagePercent));

        Assert.That(credit, Does.Contain(2), "exactly the floor is enough — the boundary belongs to the player");
    }

    /// <summary>The floor is chosen so a group genuinely sharing a mob all qualify. Eight is well past any
    /// party, so nobody working a mob together is cut out by it.</summary>
    [Test]
    public void EightWaysIsStillComfortable()
    {
        var (combat, world, pm) = NewCombat();
        var hits = new (int, int)[8];
        for (int i = 0; i < 8; i++)
        {
            Register(world, pm, i + 1);
            hits[i] = (i + 1, 125);        // 12.5% each
        }

        var credit = CreditFor(combat, world, hits);

        Assert.That(credit, Has.Count.EqualTo(8), "an even eight-way split leaves everyone above the floor");
    }

    [Test]
    public void APartnerWhoDidNothing_EarnsAlongsideThePlayerWhoDid()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2);
        Party(pm, 1, 2);

        var credit = CreditFor(combat, world, (1, 100));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1));
            Assert.That(credit, Does.Contain(2), "a healer never touches the damage ledger and must still quest");
        });
    }

    [Test]
    public void APartnerOfSomeoneBelowTheFloor_EarnsNothingEither()
    {
        var (combat, world, pm) = NewCombat();
        for (int i = 1; i <= 3; i++) Register(world, pm, i);
        Party(pm, 2, 3);   // 2 tags the mob, 3 rides along

        var credit = CreditFor(combat, world, (1, 95), (2, 5));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1));
            Assert.That(credit, Does.Not.Contain(2), "a tag earns nothing");
            Assert.That(credit, Does.Not.Contain(3), "and cannot be laundered through a partner");
        });
    }

    /// <summary>Partner reach is the same test the partner-kill EXP bonus uses — observing the map the kill
    /// happened on. A partner who is not there gets neither.</summary>
    [Test]
    public void APartnerOutOfReach_EarnsNothing()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);
        Register(world, pm, 2, mapNum: Map + 4);   // elsewhere, and not observing this map
        Party(pm, 1, 2);

        var credit = CreditFor(combat, world, (1, 100));

        Assert.Multiple(() =>
        {
            Assert.That(credit, Does.Contain(1));
            Assert.That(credit, Does.Not.Contain(2));
        });
    }

    [Test]
    public void ASoloKiller_Earns()
    {
        var (combat, world, pm) = NewCombat();
        Register(world, pm, 1);

        Assert.That(CreditFor(combat, world, (1, 1)), Does.Contain(1), "all of one point of damage is all of it");
    }

    private sealed class NoOpDispatcher : IPacketDispatcher
    {
        public void SendTo(int index, IPacket packet) { }
        public void SendToAll(IPacket packet) { }
        public void SendToAllBut(int exclude, IPacket packet) { }
        public void SendToObservers(IReadOnlyCollection<int> observers, IPacket packet) { }
        public void SendToObserversBut(IReadOnlyCollection<int> observers, int exclude, IPacket packet) { }
        public void SendToViewport(int speakerIndex, IPacket packet) { }
        public void SendToViewportAt(int mapNum, int x, int y, IPacket packet) { }
        public void SendChatBubble(int speakerIndex, IPacket packet, string senderLogin, bool wholeRegion) { }
        public void SendToAdmins(IPacket packet) { }
        public void SendToGuild(int guildId, IPacket packet) { }
        public void SendToGuildBut(int guildId, int exclude, IPacket packet) { }
        public void SendLocalizedChatToGuild(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToGuildOfficers(int guildId, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatTo(int index, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAll(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAllBut(int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObservers(IReadOnlyCollection<int> observers, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToObserversBut(IReadOnlyCollection<int> observers, int exclude, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewport(int speakerIndex, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToViewportAt(int mapNum, int x, int y, string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendLocalizedChatToAdmins(string key, ChatMetadata meta, params (string Key, object? Value)[] args) { }
        public void SendToEditor(int editorIndex, IPacket packet) { }
        public void SendToAllEditors(IPacket packet) { }
        public void Disconnect(int index) { }
        public void DisconnectEditor(int editorIndex) { }
        public void GracefulDisconnect(int index) { }
        public void GracefulDisconnectEditor(int editorIndex) { }
    }
}
