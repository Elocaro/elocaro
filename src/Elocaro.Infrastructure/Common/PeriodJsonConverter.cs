using System.Text.Json;
using System.Text.Json.Serialization;
using Elocaro.Domain.Common;

namespace Elocaro.Infrastructure.Common;

/// <summary><see cref="Period"/> as <c>{"from":"2026-01-01","to":null}</c> (without computed properties).</summary>
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
            if (string.Equals(name, "from", StringComparison.OrdinalIgnoreCase))
            {
                from = value;
            }
            else if (string.Equals(name, "to", StringComparison.OrdinalIgnoreCase))
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
        writer.WriteString("from", value.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        if (value.To is { } to)
        {
            writer.WriteString("to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNull("to");
        }

        writer.WriteEndObject();
    }
}
