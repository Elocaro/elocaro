using Elocaro.Application.Admin;
using Elocaro.Application.Operations;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Operations;
using Elocaro.Domain.Portfolio;
using Elocaro.Web.Components.Account.Login;
using Elocaro.Web.Components.Common;
using Elocaro.Web.Components.Pages.Admin.Leases;
using Elocaro.Web.Components.Pages.Admin.OperatingCosts;
using Elocaro.Web.Components.Pages.Admin.Operations;
using Elocaro.Web.Components.Pages.Admin.Settings;
using Elocaro.Web.Finance;

namespace Elocaro.Web.Tests;

/// <summary>The logic that was taken out of the large pages (#190) and that needs no browser.</summary>
public class PageLogicTests
{
    [Fact]
    public void Backup_target_without_name_and_retention_says_what_is_missing()
    {
        var missing = BackupTargetForm.Missing(new BackupSettingsInput { Name = " ", KeepDaily = 0, KeepWeekly = 0, KeepMonthly = 0 }, hasPassword: false, new HashSet<string>());

        Assert.Contains("Name", missing);
        Assert.Contains("Aufbewahrung (mindestens eine Frist)", missing);
    }

    [Theory]
    [InlineData("WIEDERHERSTELLEN", true)]
    [InlineData("  WIEDERHERSTELLEN ", true)]
    [InlineData("wiederherstellen", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Restore_needs_the_word_typed_exactly(string? entry, bool expected) => Assert.Equal(expected, BackupTexts.IsConfirmed(entry));

    [Fact]
    public void Upload_results_have_a_text_and_nothing_uploaded_has_none()
    {
        Assert.Null(BackupTexts.UploadMessage(null));
        Assert.Contains("Passwort", BackupTexts.UploadMessage("ok"), StringComparison.Ordinal);
        Assert.Contains("Einrichtungscode", RestoreSetupText.UploadMessage("code"), StringComparison.Ordinal);
        Assert.Contains("nicht hochgeladen", BackupTexts.UploadMessage("unbekannt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Rent_increase_kinds_follow_the_rent_model_and_always_include_the_selected_one()
    {
        var standard = RentIncreaseFlow.AllowedKinds(RentModel.Standard, BillingType.Prepayment, RentIncreaseKind.Comparative).ToList();
        Assert.Contains(RentIncreaseKind.Comparative, standard);
        Assert.Contains(RentIncreaseKind.Modernisation, standard);
        Assert.DoesNotContain(RentIncreaseKind.Index, standard);
        Assert.DoesNotContain(RentIncreaseKind.Graduated, standard);

        var graduated = RentIncreaseFlow.AllowedKinds(RentModel.Graduated, BillingType.Prepayment, RentIncreaseKind.Graduated).ToList();
        Assert.Equal([RentIncreaseKind.Graduated], graduated);

        Assert.Equal(RentIncreaseKind.Index, RentIncreaseFlow.DefaultKind(RentModel.Index, BillingType.Prepayment));
        Assert.Equal(RentIncreaseKind.FlatRate, RentIncreaseFlow.DefaultKind(RentModel.Standard, BillingType.FlatRate));
        Assert.Equal(RentIncreaseKind.Comparative, RentIncreaseFlow.DefaultKind(RentModel.Standard, BillingType.Prepayment));
    }

    [Fact]
    public void Billing_period_counts_its_months_and_at_least_one()
    {
        Assert.Equal(12m, StatementWizardView.MonthsOf(Period.CalendarYear(2026)));
        Assert.Equal(1m, StatementWizardView.MonthsOf(Period.Month(2026, 4)));
    }

    [Fact]
    public void Lease_form_takes_over_only_the_fields_of_the_billing_type()
    {
        var form = new LeaseForm
        {
            BillingType = BillingType.FlatRate,
            BaseRent = 600m,
            OperatingCostPrepayment = 100m,
            OperatingCostFlatRate = 80m,
            Tenant1 = Guid.NewGuid(),
        };

        var creation = form.ToCreation();

        Assert.Equal(680m, form.MonthlyTotal);
        Assert.Null(creation.OperatingCostPrepayment);
        Assert.Equal(80m, creation.OperatingCostFlatRate);
        Assert.Single(creation.TenantIds);
    }

    [Fact]
    public void Lease_form_checks_only_what_the_current_step_asks_for()
    {
        var form = new LeaseForm { Stage = 0 };
        Assert.Equal(2, form.Validate(new(form)).Count()); // unit and start

        form.Stage = 2;
        Assert.Equal(4, form.Validate(new(form)).Count()); // plus tenant and base rent
    }

    [Fact]
    public void Setup_profile_sets_the_starting_point_of_modules_and_options()
    {
        var modules = new[] { new ModuleChoice(AppModule.Tickets), new ModuleChoice(AppModule.Notices) };
        var options = new[] { new OptionChoice(AppOption.RoomRental), new OptionChoice(AppOption.RentModelIndex), new OptionChoice(AppOption.BillingPrepayment) };

        SetupWizardChoices.ApplyProfile(SetupProfile.Few, modules, options);
        Assert.All(modules, m => Assert.False(m.On));
        Assert.True(options.Single(o => o.Option == AppOption.BillingPrepayment).On);
        Assert.False(options.Single(o => o.Option == AppOption.RoomRental).On);

        SetupWizardChoices.ApplyProfile(SetupProfile.Many, modules, options);
        Assert.All(modules, m => Assert.True(m.On));
        Assert.True(options.Single(o => o.Option == AppOption.RentModelIndex).On);

        SetupWizardChoices.ApplyProfile(SetupProfile.Rooms, modules, options);
        Assert.True(options.Single(o => o.Option == AppOption.RoomRental).On);
        Assert.False(options.Single(o => o.Option == AppOption.RentModelIndex).On);
    }

    [Fact]
    public void Rent_potential_csv_has_german_numbers_a_bom_and_neutralized_text()
    {
        var row = RentPotentialCalculator.Assess(
            new RentPotentialInput(Guid.NewGuid(), "=Wohnung 1", 500m, 80m, 500m, 20, new DateOnly(2027, 8, 20), true), 10m, new DateOnly(2027, 2, 10));

        var csv = RentPotentialEndpoint.Csv([row]);
        var text = System.Text.Encoding.UTF8.GetString(csv, 3, csv.Length - 3);

        Assert.Equal([0xEF, 0xBB, 0xBF], csv[..3]);
        Assert.StartsWith("Mietobjekt;Kaltmiete;", text, StringComparison.Ordinal);
        Assert.Contains("'=Wohnung 1;500,00;80,00;6,25;800,00;600,00;600,00;100,00;Kappungsgrenze;20.08.2027;01.11.2027;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Key_figures_csv_has_a_row_per_year_with_german_numbers_and_empty_cells_for_missing_values()
    {
        var figures = PropertyKeyFigureCalculator.Calculate(new PropertyFigureInput(2025, 12000m, 14000m, 1000m, 2000m, 500m, 0m, 800m, null, 365, 73, 14000m, 0m));

        var csv = KeyFiguresEndpoint.Csv([figures]);
        var text = System.Text.Encoding.UTF8.GetString(csv, 3, csv.Length - 3);

        Assert.Equal([0xEF, 0xBB, 0xBF], csv[..3]);
        Assert.StartsWith("Jahr;Nettomietrendite (%);Cashflow vor Tilgung;Cashflow;Leerstandsquote (%);Rückstandsquote (%)\r\n", text, StringComparison.Ordinal);
        Assert.Contains("2025;;10500,00;9700,00;20,00;0,00\r\n", text, StringComparison.Ordinal);
    }
}
