using Mirage.Client.Core.Net;
using Mirage.Client.Core.State;
using Mirage.Shared;
using Mirage.Shared.Protocol.Packets;
using NUnit.Framework;
using System.Reflection;

namespace Mirage.Client.Core.Tests;

/// <summary>The blood-pool wire decode: a BloodUpdatePacket carries a map's WHOLE pool list, 6 bytes each
/// (x, y, size, amount, freshness, layer), and the client REPLACES its list for that map (so a merged-away pool
/// drops out).  Pins the byte layout + the full-list-replace semantics (a slip here would shear every pool or
/// leave ghosts).</summary>
[TestFixture]
public class BloodUpdateDecodeTests
{
    static readonly MethodInfo Handle = typeof(ClientPacketHandler)
        .GetMethod("HandleBloodUpdate", BindingFlags.NonPublic | BindingFlags.Instance)!;

    static void Apply(ClientState state, BloodUpdatePacket packet)
    {
        var handler = new ClientPacketHandler(state, null!, null!);
        Handle.Invoke(handler, new object[] { packet });
    }

    [Test]
    public void DecodesPoolList()
    {
        var state = new ClientState { CenterMapNum = 1 };
        // Two pools (6 bytes each): (5,6) size 3 full amount + full freshness on GROUND; (2,3) size 1 half amount,
        // ~0.78 freshness on FRINGE.
        Apply(state, new BloodUpdatePacket { MapNum = 1, Reset = false, Pools = new byte[] { 5, 6, 3, 255, 255, 0, 2, 3, 1, 128, 200, 1 } });
        var pools = state.BloodByMap[1];
        Assert.That(pools.Count, Is.EqualTo(2));
        Assert.That(pools[0].X, Is.EqualTo(5));
        Assert.That(pools[0].Y, Is.EqualTo(6));
        Assert.That(pools[0].Size, Is.EqualTo(3), "a big NPC's pool must decode its body size so the decal scales");
        Assert.That(pools[0].Amount, Is.EqualTo(Constants.BloodMaxTileAmount).Within(1e-3f));
        Assert.That(pools[0].Freshness, Is.EqualTo(1f).Within(1e-3f));
        Assert.That(pools[0].Layer, Is.EqualTo(WorldLayer.Ground));
        Assert.That(pools[1].Size, Is.EqualTo(1));
        Assert.That(pools[1].Freshness, Is.EqualTo(200 / 255f).Within(1e-3f));
        Assert.That(pools[1].Layer, Is.EqualTo(WorldLayer.Fringe), "the 6th byte decodes the layer");
    }

    [Test]
    public void FullListReplace_SwapsNotAppends()
    {
        var state = new ClientState { CenterMapNum = 1 };
        Apply(state, new BloodUpdatePacket { MapNum = 1, Pools = new byte[] { 5, 6, 3, 255, 255, 0 } });
        Apply(state, new BloodUpdatePacket { MapNum = 1, Pools = new byte[] { 2, 3, 1, 128, 200, 0 } });
        Assert.That(state.BloodByMap[1].Count, Is.EqualTo(1), "a full-list update REPLACES the map's pools, not appends");
        Assert.That(state.BloodByMap[1][0].X, Is.EqualTo(2), "the surviving pool is from the latest packet");
    }

    [Test]
    public void UnobservedMap_IsIgnored()
    {
        var state = new ClientState { CenterMapNum = 1 };   // map 9 is not the center nor a neighbor
        Apply(state, new BloodUpdatePacket { MapNum = 9, Pools = new byte[] { 5, 6, 3, 255, 255, 0 } });
        Assert.That(state.BloodByMap.ContainsKey(9), Is.False, "blood for a map we aren't observing is dropped");
    }
}
