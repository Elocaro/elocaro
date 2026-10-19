using System.Text.Json;
using System.Text.Json.Serialization;
using MietHahn.Domain.Common;

namespace MietHahn.Infrastructure.Common;

/// <summary><see cref="Period"/> as <c>{"von":"2026-01-01","bis":null}</c> (without computed properties).</summary>
internal sealed class PeriodJsonConverter : JsonConverter<Period>
{
    public override Period Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        DateOnly? from = null, to = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            var value = reader.TokenType == JsonTokenType.Null ? (DateOnly?)null : DateOnly.Parse(reader.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            if (string.Equals(name, "von", StringComparison.OrdinalIgnoreCase))
            {
                from = value;
            }
            else if (string.Equals(name, "bis", StringComparison.OrdinalIgnoreCase))
            {
                to = value;
            }
        }

        return new Period(from ?? throw new JsonException("Zeitraum ohne Beginn."), to);
    }

    public override void Write(Utf8JsonWriter writer, Period value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartObject();
        writer.WriteString("von", value.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (value.To is { } to)
        {
            writer.WriteString("bis", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull("bis");
        }

        writer.WriteEndObject();
    }
}
