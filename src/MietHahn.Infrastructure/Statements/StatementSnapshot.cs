using System.Text.Json;
using System.Text.Json.Serialization;
using MietHahn.Domain.Common;
using MietHahn.Domain.OperatingCosts;

namespace MietHahn.Infrastructure.Statements;

/// <summary>Serialization of the calculation result that is frozen on release.</summary>
internal static class StatementSnapshot
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(), new PeriodConverter() },
        WriteIndented = false,
    };

    public static string Serialize(BillingResult result) => JsonSerializer.Serialize(result, Options);

    public static BillingResult Deserialize(string snapshot) =>
        JsonSerializer.Deserialize<BillingResult>(snapshot, Options) ?? throw new InvalidOperationException("Der gespeicherte Abrechnungsstand ist leer.");

    /// <summary>Only the boundaries: the computed members of <see cref="Period"/> (e.g. <c>Days</c>) fail for an open period.</summary>
    private sealed class PeriodConverter : JsonConverter<Period>
    {
        public override Period Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            var to = root.GetProperty("to");
            return new Period(DateOnly.Parse(root.GetProperty("from").GetString()!, System.Globalization.CultureInfo.InvariantCulture), to.ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(to.GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        }

        public override void Write(Utf8JsonWriter writer, Period value, JsonSerializerOptions options)
        {
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
}
