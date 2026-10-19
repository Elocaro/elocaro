using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MietHahn.Application.Audit;
using MietHahn.Application.Files;
using MietHahn.Application.People;
using MietHahn.Application.Properties;
using MietHahn.Application.Receipts;
using MietHahn.Domain.People;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Receipts.Extraction;
using MietHahn.Integration.Tests.Infrastructure;

namespace MietHahn.Integration.Tests.Receipts.Extraction;

/// <summary>The extraction use case with the real infrastructure (DB, file store, DI) and a fake provider server.</summary>
public class ReceiptExtractionTests(PostgresFixture postgres)
{
    private const string Key = "sk-ant-integration-secret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object ModelInput(string supplier = "Grünpflege Meier", string? costType = "betrkv-10", double gross = 595, double net = 500, double vat = 95) => new
    {
        supplier_name = new { value = supplier, confidence = 0.97 },
        invoice_number = new { value = "R-117", confidence = 0.99 },
        invoice_date = new { value = "2026-02-27", confidence = 0.98 },
        gross = new { value = gross, confidence = 0.99 },
        net = new { value = net, confidence = 0.99 },
        vat = new { value = vat, confidence = 0.99 },
        cost_type = new { value = costType, confidence = 0.6 },
    };

    private static Dictionary<string, string?> Claude(bool consent = true) => new()
    {
        ["ReceiptExtraction:Provider"] = "Claude",
        ["ReceiptExtraction:AcceptExternalProcessing"] = consent ? "true" : "false",
        ["ReceiptExtraction:Claude:ApiKey"] = Key,
        ["ReceiptExtraction:Claude:BaseUrl"] = "https://claude.test/",
    };

    private async Task<InfrastructureEnvironment> EnvironmentAsync(IDictionary<string, string?>? configuration, FakeHttpHandler handler) =>
        await InfrastructureEnvironment.CreateAsync(postgres, configuration, services =>
        {
            services.AddHttpClient<ClaudeReceiptExtractor>().ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddHttpClient<OllamaReceiptExtractor>().ConfigurePrimaryHttpMessageHandler(() => handler);
        });

    private static async Task<Guid> FileAsync(InfrastructureEnvironment environment, string mime = "pdf")
    {
        await using var scope = environment.Services.CreateAsyncScope();
        var content = mime == "pdf" ? TestFiles.Pdf("Rechnung") : TestFiles.TwoColorImage(40, 40);
        return (await scope.ServiceProvider.GetRequiredService<IFileStore>().SaveAsync(new MemoryStream(content), mime == "pdf" ? "rechnung.pdf" : "foto.jpg", Ct)).Id;
    }

    private static async Task<ExtractionOutcome> SuggestAsync(InfrastructureEnvironment environment, params Guid[] files)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReceiptExtraction>().SuggestAsync(files, Ct);
    }

    [Theory]
    [InlineData("None", "Keiner", false)]
    [InlineData("Claude", "Claude API", true)]
    [InlineData("Ollama", "Ollama", false)]
    [InlineData("", "Keiner", false)]
    public async Task Provider_is_chosen_by_configuration_alone(string provider, string name, bool thirdParty)
    {
        var configuration = Claude();
        configuration["ReceiptExtraction:Provider"] = provider;
        await using var environment = await EnvironmentAsync(configuration, FakeHttpHandler.Json("{}"));

        await using var scope = environment.Services.CreateAsyncScope();
        var info = scope.ServiceProvider.GetRequiredService<IReceiptExtractor>().Info;

        Assert.Equal(name, info.Name);
        Assert.Equal(thirdParty, info.SendsToThirdParty);
    }

    [Fact]
    public async Task Default_configuration_is_none_and_extraction_is_refused()
    {
        var handler = FakeHttpHandler.Json("{}");
        await using var environment = await EnvironmentAsync(null, handler);
        var file = await FileAsync(environment);

        var outcome = await SuggestAsync(environment, file);

        Assert.Null(outcome.Suggestion);
        Assert.Contains("nicht aktiviert", outcome.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Claude_without_explicit_consent_sends_nothing()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ModelInput()));
        await using var environment = await EnvironmentAsync(Claude(consent: false), handler);

        var outcome = await SuggestAsync(environment, await FileAsync(environment));

        Assert.Contains("nicht freigegeben", outcome.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Suggestion_contains_checked_values_usage_and_matches_the_supplier_contact()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ModelInput(supplier: "GRÜNPFLEGE  Meier gmbh"), 2000, 400));
        await using var environment = await EnvironmentAsync(Claude(), handler);
        var contact = await ContactAsync(environment, "Grünpflege Meier GmbH");
        var file = await FileAsync(environment);

        var outcome = await SuggestAsync(environment, file);

        Assert.Null(outcome.Error);
        Assert.Equal(contact, outcome.SupplierId);
        Assert.Equal(595m, outcome.Suggestion!.Gross.Value);
        Assert.Equal(new DateOnly(2026, 2, 27), outcome.Suggestion.InvoiceDate.Value);
        Assert.Equal("betrkv-10", outcome.Suggestion.CostType.Value);
        Assert.True(outcome.Suggestion.CostType.IsUncertain);
        Assert.Contains("CostType", outcome.Suggestion.UncertainFields());
        Assert.Empty(outcome.Suggestion.Warnings);
        Assert.Equal(new ExtractionUsage(2000, 400), outcome.Usage);
        Assert.Single(handler.Requests);
        Assert.Equal(Key, handler.Requests[0].Headers["x-api-key"]);
    }

    [Fact]
    public async Task Suggestion_is_not_saved_anywhere_and_the_key_is_not_in_the_database()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ModelInput()));
        await using var environment = await EnvironmentAsync(Claude(), handler);
        var file = await FileAsync(environment);

        await SuggestAsync(environment, file);

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        Assert.Empty(await db.Set<Receipt>().ToListAsync(Ct));
        Assert.Empty(await db.Set<CostItem>().ToListAsync(Ct));
        Assert.Empty(await db.Set<SupplierCostTypeRule>().ToListAsync(Ct));
        var entries = await db.Set<AuditEntry>().AsNoTracking().ToListAsync(Ct);
        Assert.All(entries, e => Assert.DoesNotContain(Key, e.ChangesJson + e.Purpose + e.EntityType, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transfer_to_the_provider_is_recorded_in_the_access_log()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ModelInput()));
        await using var environment = await EnvironmentAsync(Claude(), handler);
        var file = await FileAsync(environment);

        await SuggestAsync(environment, file);

        await using var scope = environment.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MietHahnDbContext>();
        var entry = await db.Set<AuditEntry>().AsNoTracking().SingleAsync(e => e.Action == AuditAction.Read, Ct);
        Assert.Equal(file, entry.EntityId);
        Assert.Contains("Claude API", entry.Purpose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Implausible_model_output_comes_with_warnings()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ModelInput(gross: 595, net: 500, vat: 80)));
        await using var environment = await EnvironmentAsync(Claude(), handler);

        var outcome = await SuggestAsync(environment, await FileAsync(environment));

        Assert.Contains(outcome.Suggestion!.Warnings, w => w.Contains("ergibt nicht den Bruttobetrag", StringComparison.Ordinal));
        Assert.True(outcome.Suggestion.Gross.IsUncertain);
    }

    [Fact]
    public async Task Provider_failures_and_unreadable_answers_become_error_messages()
    {
        await using var down = await EnvironmentAsync(Claude(), FakeHttpHandler.Status(System.Net.HttpStatusCode.ServiceUnavailable));
        await using var garbage = await EnvironmentAsync(Claude(), FakeHttpHandler.Json(FakeHttpHandler.ClaudeTextResponse("kein json")));

        var failed = await SuggestAsync(down, await FileAsync(down));
        var unreadable = await SuggestAsync(garbage, await FileAsync(garbage));

        Assert.Null(failed.Suggestion);
        Assert.Contains("meldet einen Fehler", failed.Error, StringComparison.Ordinal);
        Assert.Null(unreadable.Suggestion);
        Assert.Contains("Antwort des Modells", unreadable.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_file_and_empty_list_are_rejected_without_calling_the_provider()
    {
        var handler = FakeHttpHandler.Json("{}");
        await using var environment = await EnvironmentAsync(Claude(), handler);

        var unknown = await SuggestAsync(environment, Guid.CreateVersion7());
        var empty = await SuggestAsync(environment);

        Assert.Contains("nicht gefunden", unknown.Error, StringComparison.Ordinal);
        Assert.Contains("Datei", empty.Error, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Cost_type_is_learned_from_confirmed_receipts_and_overrides_the_model()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ModelInput(supplier: "Hausmeister Schulz", costType: "betrkv-14")));
        await using var environment = await EnvironmentAsync(Claude(), handler);
        var property = await PropertyAsync(environment);

        // Before: the model's suggestion is used as is.
        Assert.Equal("betrkv-14", (await SuggestAsync(environment, await FileAsync(environment))).Suggestion!.CostType.Value);

        // The user books the receipts of this supplier as "Instandhaltung" (name only, no contact).
        for (var i = 0; i < 2; i++)
        {
            await using var scope = environment.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierName = "Hausmeister  Schulz", InvoiceDate = new DateOnly(2026, 4, 1 + i), Gross = 100m },
                [], new CostItemInput { CostType = "instandhaltung", PropertyId = property }, Ct);
            Assert.True(result.Succeeded);
        }

        var outcome = await SuggestAsync(environment, await FileAsync(environment));

        Assert.Equal("instandhaltung", outcome.Suggestion!.CostType.Value);
        Assert.False(outcome.Suggestion.CostType.IsUncertain);
        Assert.Contains("2 früheren Belegen", outcome.Suggestion.CostTypeReason, StringComparison.Ordinal);

        await using var check = environment.Services.CreateAsyncScope();
        var rule = await check.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<SupplierCostTypeRule>().SingleAsync(Ct);
        Assert.Equal(("name:hausmeister schulz", "instandhaltung", 2), (rule.SupplierKey, rule.CostType, rule.Uses));
    }

    [Fact]
    public async Task Rule_of_a_contact_is_found_by_the_contact_and_follows_a_changed_choice()
    {
        var handler = FakeHttpHandler.Json(FakeHttpHandler.ClaudeToolResponse(ModelInput(supplier: "Grünpflege Meier GmbH", costType: null)));
        await using var environment = await EnvironmentAsync(Claude(), handler);
        var contact = await ContactAsync(environment, "Grünpflege Meier GmbH");
        var property = await PropertyAsync(environment);
        foreach (var costType in new[] { "betrkv-10", "instandhaltung" })
        {
            await using var scope = environment.Services.CreateAsyncScope();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IReceiptService>().CreateAsync(
                new ReceiptInput { SupplierId = contact, InvoiceDate = new DateOnly(2026, 4, 1), Gross = 50m }, [],
                new CostItemInput { CostType = costType, PropertyId = property }, Ct)).Succeeded);
        }

        var outcome = await SuggestAsync(environment, await FileAsync(environment));

        Assert.Equal(contact, outcome.SupplierId);
        Assert.Equal("instandhaltung", outcome.Suggestion!.CostType.Value);
        Assert.Contains("einem früheren Beleg", outcome.Suggestion.CostTypeReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Receipts_without_cost_item_or_supplier_learn_nothing()
    {
        await using var environment = await EnvironmentAsync(null, FakeHttpHandler.Json("{}"));
        await using var scope = environment.Services.CreateAsyncScope();
        var receipts = scope.ServiceProvider.GetRequiredService<IReceiptService>();

        Assert.True((await receipts.CreateAsync(new ReceiptInput { SupplierName = "X", InvoiceDate = new DateOnly(2026, 1, 1), Gross = 5m }, [], null, Ct)).Succeeded);

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<MietHahnDbContext>().Set<SupplierCostTypeRule>().ToListAsync(Ct));
    }

    private static async Task<Guid> PropertyAsync(InfrastructureEnvironment environment)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPropertyService>().CreateAsync(
            new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
    }

    private static async Task<Guid> ContactAsync(InfrastructureEnvironment environment, string company)
    {
        await using var scope = environment.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IPersonService>().CreateAsync(
            new PersonInput { LastName = company, Company = company, ContactType = ContactType.ServiceProvider }, null, Ct)).Id!.Value;
    }
}
