using System.Text.Json;
using System.Text.Json.Serialization;
using MietHahn.Domain.Gemeinsam;

namespace MietHahn.Infrastructure.Gemeinsam;

/// <summary><see cref="Zeitraum"/> als <c>{"von":"2026-01-01","bis":null}</c> (ohne berechnete Eigenschaften).</summary>
internal sealed class ZeitraumJsonConverter : JsonConverter<Zeitraum>
{
    public override Zeitraum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        DateOnly? von = null, bis = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            var wert = reader.TokenType == JsonTokenType.Null ? (DateOnly?)null : DateOnly.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            if (string.Equals(name, "von", StringComparison.OrdinalIgnoreCase))
            {
                von = wert;
            }
            else if (string.Equals(name, "bis", StringComparison.OrdinalIgnoreCase))
            {
                bis = wert;
            }
        }

        return new Zeitraum(von ?? throw new JsonException("Zeitraum ohne Beginn."), bis);
    }

    public override void Write(Utf8JsonWriter writer, Zeitraum value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        writer.WriteString("von", value.Von.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (value.Bis is { } bis)
        {
            writer.WriteString("bis", bis.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull("bis");
        }

        writer.WriteEndObject();
    }
}
