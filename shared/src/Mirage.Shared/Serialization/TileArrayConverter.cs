using Mirage.Shared.Records;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Serialization;

/// <summary>
/// Serializes <see cref="TileRecord"/>[,] as a JSON array-of-arrays because
/// System.Text.Json has no built-in support for multi-dimensional arrays.
/// Outer array = X (column), inner array = Y (row).
/// </summary>
internal sealed class TileArrayConverter : JsonConverter<TileRecord[,]>
{
    public override TileRecord[,] Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var arr = new TileRecord[Constants.MaxMapX + 1, Constants.MaxMapY + 1];
        for (int x = 0; x <= Constants.MaxMapX; x++)
        {
            for (int y = 0; y <= Constants.MaxMapY; y++)
                arr[x, y] = new TileRecord();
        }

        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
        reader.Read();

        for (int x = 0; reader.TokenType != JsonTokenType.EndArray; x++)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
            reader.Read();

            for (int y = 0; reader.TokenType != JsonTokenType.EndArray; y++)
            {
                var tile = JsonSerializer.Deserialize<TileRecord>(ref reader, options)
                           ?? new TileRecord();
                if (x <= Constants.MaxMapX && y <= Constants.MaxMapY)
                    arr[x, y] = tile;
                reader.Read();
            }
            reader.Read(); // past inner EndArray
        }
        // Reader is now at outer EndArray; framework advances past it.
        return arr;
    }

    public override void Write(
        Utf8JsonWriter writer, TileRecord[,] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        int cols = value.GetLength(0);
        int rows = value.GetLength(1);
        for (int x = 0; x < cols; x++)
        {
            writer.WriteStartArray();
            for (int y = 0; y < rows; y++)
                JsonSerializer.Serialize(writer, value[x, y], options);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }
}
