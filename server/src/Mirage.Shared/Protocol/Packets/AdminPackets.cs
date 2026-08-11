using System.Text.Json.Serialization;

namespace Mirage.Shared.Protocol.Packets;

public sealed record WarpMeToPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.WarpMeTo;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record WarpToMePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.WarpToMe;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record WarpToPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.WarpTo;
    [JsonPropertyName("mapNum")] public int MapNum { get; init; }
}

public sealed record SetSpritePacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetSprite;
    [JsonPropertyName("sprite")] public int Sprite { get; init; }
}

public sealed record SetAccessPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetAccess;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("level")] public AdminLevel Level { get; init; }
}

public sealed record KickPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.KickPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("minutes")] public int Minutes { get; init; }
}

public sealed record BanPlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.BanPlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

public sealed record MutePlayerPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MutePlayer;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
    [JsonPropertyName("minutes")] public int Minutes { get; init; }
}

public sealed record RefreshBanListPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.RefreshBanList;
}

public sealed record MapRespawnPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapRespawn;
}

public sealed record MapReportPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.MapReport;
}

public sealed record SetMotdPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetMotd;
    [JsonPropertyName("msg")] public string Motd { get; init; } = "";
}

public sealed record SetTimeOfDayPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetTimeOfDay;
    [JsonPropertyName("phase")] public TimePhase Phase { get; init; }
}

public sealed record SetWeatherPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.SetWeather;
    [JsonPropertyName("weather")] public WeatherType Weather { get; init; }
}

public sealed record PlayerInfoRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayerInfoRequest;
    [JsonPropertyName("target")] public string Target { get; init; } = "";
}

/// <summary>C->S: /played — the requester's own playtime readout (current character + account total). No
/// target; playtime is not admin-gated (account details are visible by design).</summary>
public sealed record PlayedRequestPacket : IPacket
{
    [JsonPropertyName("cmd")] public string Cmd => PacketNames.PlayedRequest;
}
