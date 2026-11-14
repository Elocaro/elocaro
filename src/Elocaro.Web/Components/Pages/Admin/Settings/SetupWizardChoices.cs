using Elocaro.Application.Admin;
using Elocaro.Web.Components.Common;

namespace Elocaro.Web.Components.Pages.Admin.Settings;

/// <summary>The answer to "Wie vermieten Sie?" in the setup wizard: a starting point for modules and options.</summary>
public enum SetupProfile
{
    Few,
    Many,
    Rooms,
}

/// <summary>A module with its switch in the setup wizard.</summary>
public sealed class ModuleChoice(AppModule module)
{
    public AppModule Module { get; } = module;

    public bool On { get; set; }
}

/// <summary>An option with its switch in the setup wizard.</summary>
public sealed class OptionChoice(AppOption option)
{
    public AppOption Option { get; } = option;

    public bool On { get; set; }
}

/// <summary>What the setup wizard decides without a browser: the starting point per profile, switching modules together with what they need, the texts.</summary>
public static class SetupWizardChoices
{
    public static string ProfileText(SetupProfile value) => value switch
    {
        SetupProfile.Many => "Ich habe einen größeren Bestand oder einen Hausmeister",
        SetupProfile.Rooms => "Ich vermiete einzelne Zimmer (WG)",
        _ => "Ich vermiete selbst einige Wohnungen",
    };

    public static string GroupQuestion(string group) => group switch
    {
        ModuleCatalog.RentModelGroup => "Welche Mietmodelle verwenden Sie? Die Vergleichsmiete (normale Miete mit Erhöhung nach § 558 BGB) ist immer dabei.",
        ModuleCatalog.DunningGroup => "Wie soll das Mahnwesen arbeiten? Mahnungen werden immer nur vorgeschlagen und erst nach Ihrer Freigabe versendet.",
        ModuleCatalog.UnitsGroup => "Was vermieten Sie? Wohnungen sind immer dabei.",
        ModuleCatalog.LeaseGroup => "Welche Angaben möchten Sie im Mietvertrag erfassen?",
        ModuleCatalog.TaxGroup => "Welche Teile der Steuer-Vorbereitung brauchen Sie? Die Anlage V selbst ist immer dabei.",
        _ => "Wie rechnen Sie die Nebenkosten mit Ihren Mietern ab? Wählen Sie alles, was vorkommt.",
    };

    /// <summary>
    /// A starting point for the modules and the options, from the answer to "Wie vermieten Sie?". The next steps let the user change every
    /// one of them; nothing is chosen "for everything".
    /// </summary>
    public static void ApplyProfile(SetupProfile profile, IReadOnlyList<ModuleChoice> modules, IReadOnlyList<OptionChoice> options)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(options);
        var off = profile == SetupProfile.Many ? [] : new[] { AppModule.Tickets, AppModule.Notices };
        foreach (var choice in modules)
        {
            choice.On = !off.Contains(choice.Module);
        }

        HashSet<AppOption> on = [AppOption.BillingPrepayment, AppOption.TaxLoans, AppOption.TaxDepreciation, AppOption.LeaseDeposit];
        switch (profile)
        {
            case SetupProfile.Many:
                on.UnionWith([AppOption.RentModelGraduated, AppOption.RentModelIndex, AppOption.TaxDatev, AppOption.UnitParking, AppOption.LeaseGuarantor]);
                break;
            case SetupProfile.Rooms:
                on.UnionWith([AppOption.RoomRental, AppOption.BillingFlatRate]);
                break;
        }

        foreach (var option in options)
        {
            option.On = on.Contains(option.Option);
        }
    }

    /// <summary>Switches a module; on also switches on what it needs, off also switches off what needs it.</summary>
    public static void Switch(IReadOnlyList<ModuleChoice> modules, ModuleChoice choice, bool on)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(choice);
        choice.On = on;
        if (on)
        {
            foreach (var required in ModuleCatalog.WithRequirements([choice.Module]))
            {
                modules.First(c => c.Module == required).On = true;
            }
        }
        else
        {
            foreach (var other in modules.Where(c => c.On && ModuleCatalog.WithRequirements([c.Module]).Contains(choice.Module)))
            {
                other.On = false;
            }
        }
    }
}

/// <summary>The steps of the setup wizard; steps that do not apply to the installation are left out.</summary>
public enum SetupStep
{
    Welcome,
    Features,
    Rental,
    Profile,
    Property,
    Books,
    Summary,
}
