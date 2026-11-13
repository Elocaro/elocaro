using Elocaro.Application.Documents;
using Elocaro.Domain.Documents;
using Elocaro.Infrastructure.Documents;
using Elocaro.Infrastructure.Templates;

namespace Elocaro.Integration.Tests.Documents;

/// <summary>The bundled letters for conflicts (warning, terminations, payment reminder): they render with the sample data and carry the legal basis and the notices.</summary>
public class ConflictLettersTests
{
    private static string Render(string name, object? model = null)
    {
        var snippets = TemplateDefaults.All.Where(e => e.Kind == TemplateKind.Snippet).ToDictionary(e => e.Name, e => e.Body, StringComparer.Ordinal);
        var entry = TemplateDefaults.All.Single(e => e.Name.StartsWith(name, StringComparison.Ordinal));
        return ScribanRenderer.RenderText(entry.Body, model ?? TemplateCatalog.Sample(TemplateKind.Letter), null, entry.Name, snippets).Html;
    }

    [Fact]
    public void The_warning_names_the_legal_basis_and_asks_to_stop_the_behaviour()
    {
        var html = Render("Abmahnung");

        Assert.Contains("§ 541 BGB", html, StringComparison.Ordinal);
        Assert.Contains("Wohnung 1, Lindenweg 3", html, StringComparison.Ordinal);
        Assert.Contains("ab sofort zu unterlassen", html, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_termination_for_arrears_is_immediate_and_alternatively_ordinary_with_the_hints()
    {
        var html = Render("Kündigung Zahlungsverzug");

        Assert.Contains("§ 543 Abs. 2 Satz 1 Nr. 3 BGB", html, StringComparison.Ordinal);
        Assert.Contains("§ 573 Abs. 2 Nr. 1 BGB", html, StringComparison.Ordinal);
        Assert.Contains("§ 569 Abs. 3 Nr. 2 BGB", html, StringComparison.Ordinal); // Schonfrist
        Assert.Contains("§§ 574 bis 574b BGB", html, StringComparison.Ordinal); // right to object
    }

    [Fact]
    public void The_arrears_amount_is_taken_from_the_tenant_account_only_if_there_is_one()
    {
        var withArrears = TemplateCatalog.Sample(TemplateKind.Letter) is LetterModel sample ? sample with { OpenArrears = 1234.5m } : throw new InvalidOperationException();
        var none = TemplateCatalog.Sample(TemplateKind.Letter) is LetterModel s2 ? s2 with { OpenArrears = 0m } : throw new InvalidOperationException();

        Assert.Contains("1.234,50", Render("Kündigung Zahlungsverzug", withArrears), StringComparison.Ordinal);
        Assert.Contains("[Betrag]", Render("Kündigung Zahlungsverzug", none), StringComparison.Ordinal);
    }

    [Fact]
    public void The_termination_for_own_use_demands_a_reason_and_points_to_the_right_to_object()
    {
        var html = Render("Kündigung Eigenbedarf");

        Assert.Contains("§ 573 Abs. 1 und Abs. 2 Nr. 2 BGB", html, StringComparison.Ordinal);
        Assert.Contains("Begründung:", html, StringComparison.Ordinal);
        Assert.Contains("[Name der Bedarfsperson]", html, StringComparison.Ordinal);
        Assert.Contains("Widerspruchsrecht", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_payment_reminder_includes_the_bank_details_block_of_the_snippet()
    {
        var html = Render("Erinnerung Nachzahlung");

        Assert.Contains("Nachzahlung", html, StringComparison.Ordinal);
        Assert.Contains("DE89 3704 0044 0532 0130 00", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_conflict_letter_carries_the_no_legal_advice_note_for_the_editor()
    {
        foreach (var name in new[] { "Abmahnung", "Kündigung Zahlungsverzug", "Kündigung Eigenbedarf", "Erinnerung Nachzahlung" })
        {
            var entry = TemplateDefaults.All.Single(e => e.Name.StartsWith(name, StringComparison.Ordinal));
            Assert.Contains("keine Rechtsberatung", entry.Body, StringComparison.Ordinal);
            Assert.Contains("Rechtsgrundlage", entry.Body, StringComparison.Ordinal);
        }
    }
}
