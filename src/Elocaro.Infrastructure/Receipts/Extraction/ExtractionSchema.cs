using System.Text.Json.Nodes;
using Elocaro.Domain.Receipts;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>Prompt and JSON schema that all providers share; the response parser reads exactly this shape.</summary>
internal static class ExtractionSchema
{
    public const string ToolName = "record_receipt";

    public static readonly IReadOnlyList<string> Section35aValues = ["none", "household_service", "tradesman_service"];

    public static string SystemPrompt(DateOnly today)
    {
        var costTypes = string.Join('\n', CostTypes.All.Select(k => $"- {k.Key}: {k.Name}"));
        return $"""
            You read German invoices and receipts of a landlord (property management) and extract the data for bookkeeping.
            Rules:
            - Only report what is printed on the document. Never guess or invent values; use null when something is missing or unreadable.
            - The recipient is the landlord, the supplier is the issuer of the invoice (company that wants money).
            - Amounts are numbers with a dot as decimal separator (1234.56), in euros. A credit note has negative amounts.
            - gross = total payable amount incl. VAT; net = total without VAT; vat = total VAT amount.
            - Dates as YYYY-MM-DD. service_from/service_to is the period of the delivered service (e.g. billing year of a utility bill).
            - cost_type: the best matching key from the list below, or null if unclear.
            - section_35a: "household_service" or "tradesman_service" only if the invoice shows labor costs separately (§ 35a EStG); labor_costs are the labor, machine and travel costs incl. VAT.
            - fuel_quantity, fuel_unit (l, kWh, kg, m³) and co2_kg only for fuel invoices (heating oil, gas, pellets); co2_kg is the CO2 emission stated on the invoice.
            - Give a confidence between 0 and 1 for every field; use values below 0.75 when the text is blurry, ambiguous or you had to infer something.
            - lines: the invoice line items with description, amount and, if clear, a cost_type.
            Today is {today:yyyy-MM-dd}. Available cost types:
            {costTypes}
            Answer only with the structured result.
            """;
    }

    public const string UserPrompt = "Extract the receipt data from the attached document pages (all pages belong to one invoice).";

    public static JsonObject Schema()
    {
        var properties = new JsonObject
        {
            ["supplier_name"] = Field("string"),
            ["supplier_iban"] = Field("string"),
            ["invoice_number"] = Field("string"),
            ["invoice_date"] = Field("string"),
            ["service_from"] = Field("string"),
            ["service_to"] = Field("string"),
            ["due_date"] = Field("string"),
            ["gross"] = Field("number"),
            ["net"] = Field("number"),
            ["vat"] = Field("number"),
            ["cost_type"] = Field("string", CostTypes.All.Select(k => k.Key)),
            ["section_35a"] = Field("string", Section35aValues),
            ["labor_costs"] = Field("number"),
            ["fuel_quantity"] = Field("number"),
            ["fuel_unit"] = Field("string"),
            ["co2_kg"] = Field("number"),
            ["lines"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["description"] = new JsonObject { ["type"] = "string" },
                        ["amount"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
                        ["cost_type"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                    },
                    ["required"] = new JsonArray("description"),
                },
            },
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray("supplier_name", "invoice_date", "gross"),
        };
    }

    /// <summary>A field is an object with value (or null) and confidence.</summary>
    private static JsonObject Field(string type, IEnumerable<string>? allowed = null)
    {
        var value = new JsonObject { ["type"] = new JsonArray(type, "null") };
        if (allowed is not null)
        {
            var values = new JsonArray();
            foreach (var item in allowed)
            {
                values.Add(item);
            }

            values.Add(null);
            value["enum"] = values;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["value"] = value,
                ["confidence"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
            },
            ["required"] = new JsonArray("value", "confidence"),
        };
    }
}
