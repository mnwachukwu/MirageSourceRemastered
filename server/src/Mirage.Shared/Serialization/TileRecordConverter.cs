using Mirage.Shared.Records;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Serialization;

/// <summary>
/// (De)serializes <see cref="TileRecord"/>.
///
/// <para>Writes the new compact shape: per-layer-type arrays of packed <see cref="LayerCell"/> values
/// (trailing empties trimmed, and the property omitted entirely when a layer type is all-empty), and
/// omits Type/Data fields at their defaults — so a blank tile serializes as just <c>{}</c>.</para>
///
/// <para>Reads that shape back.  Property names are matched case-insensitively and <c>type</c> is
/// accepted as either an enum name or a number, so the converter does not depend on the caller's
/// options (the server uses camelCase + string enums; the editor uses PascalCase + numeric).</para>
/// </summary>
internal sealed class TileRecordConverter : JsonConverter<TileRecord>
{
    public override TileRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        var tile = new TileRecord();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            string name = reader.GetString()!.ToLowerInvariant();
            reader.Read(); // advance to the value

            switch (name)
            {
                case "ground":
                    ReadPackedArray(ref reader, tile.Ground);
                    break;
                case "fringe":
                    ReadPackedArray(ref reader, tile.Fringe);
                    break;
                case "canopy":
                    ReadPackedArray(ref reader, tile.Canopy);
                    break;
                case "fringeattr":
                    tile.FringeAttr = ReadFringeAttr(ref reader);
                    break;
                case "type":
                    tile.Type = ReadTileType(ref reader);
                    break;
                case "data1":
                    tile.Data1 = reader.GetInt16();
                    break;
                case "data2":
                    tile.Data2 = reader.GetInt16();
                    break;
                case "data3":
                    tile.Data3 = reader.GetInt16();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return tile;
    }

    public override void Write(Utf8JsonWriter writer, TileRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WritePackedArray(writer, "ground", value.Ground);
        WritePackedArray(writer, "fringe", value.Fringe);
        WritePackedArray(writer, "canopy", value.Canopy);
        if (value.Type != TileType.Walkable) writer.WriteString("type", value.Type.ToString());
        if (value.Data1 != 0) writer.WriteNumber("data1", value.Data1);
        if (value.Data2 != 0) writer.WriteNumber("data2", value.Data2);
        if (value.Data3 != 0) writer.WriteNumber("data3", value.Data3);
        if (value.FringeAttr is { } fa) WriteFringeAttr(writer, fa);
        writer.WriteEndObject();
    }

    // Reads a JSON array of packed layer ints (reader positioned at StartArray) into dest, clamped to length.
    private static void ReadPackedArray(ref Utf8JsonReader reader, int[] dest)
    {
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int v = reader.GetInt32();
            if (i < dest.Length) dest[i] = v;
            i++;
        }
    }

    // Writes the layer stack up to its last non-empty slot; omits the property when fully empty.
    private static void WritePackedArray(Utf8JsonWriter writer, string name, int[] layers)
    {
        int last = -1;
        for (int i = 0; i < layers.Length; i++)
            if (!LayerCell.IsEmpty(layers[i])) last = i;
        if (last < 0) return;
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        for (int i = 0; i <= last; i++) writer.WriteNumberValue(layers[i]);
        writer.WriteEndArray();
    }

    private static TileType ReadTileType(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number) return (TileType)reader.GetInt32();
        if (reader.TokenType == JsonTokenType.String)
        {
            string s = reader.GetString()!;
            if (Enum.TryParse<TileType>(s, ignoreCase: true, out var t)) return t;
            if (int.TryParse(s, out int n)) return (TileType)n;
        }
        return TileType.Walkable;
    }

    private static FringeAttr ReadFringeAttr(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        var fa = new FringeAttr();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            string name = reader.GetString()!.ToLowerInvariant();
            reader.Read(); // advance to the value
            switch (name)
            {
                case "type":
                    fa.Type = ReadTileType(ref reader);
                    break;
                case "data1":
                    fa.Data1 = reader.GetInt16();
                    break;
                case "data2":
                    fa.Data2 = reader.GetInt16();
                    break;
                case "data3":
                    fa.Data3 = reader.GetInt16();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return fa;
    }

    // A non-null FringeAttr emits the object; its mere PRESENCE marks "a walkable fringe layer exists
    // here" (so a plain walkable fringe is the compact {}).  Default fields inside are omitted, matching
    // the tile's own Type/Data handling.
    private static void WriteFringeAttr(Utf8JsonWriter writer, FringeAttr fa)
    {
        writer.WritePropertyName("fringeAttr");
        writer.WriteStartObject();
        if (fa.Type != TileType.Walkable) writer.WriteString("type", fa.Type.ToString());
        if (fa.Data1 != 0) writer.WriteNumber("data1", fa.Data1);
        if (fa.Data2 != 0) writer.WriteNumber("data2", fa.Data2);
        if (fa.Data3 != 0) writer.WriteNumber("data3", fa.Data3);
        writer.WriteEndObject();
    }
}
