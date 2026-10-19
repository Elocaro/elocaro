using MietHahn.Domain.Bank;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Finance;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;

namespace MietHahn.Web.Display;

/// <summary>German display names for domain enumerations.</summary>
public static class Labels
{
    public static string From(Section35a value) => value switch
    {
        Section35a.HouseholdService => "Haushaltsnahe Dienstleistung",
        Section35a.TradesmanService => "Handwerkerleistung",
        _ => "keine",
    };

    public static string From(AccountType value) => value switch
    {
        AccountType.RentAccount => "Mietkonto",
        AccountType.Loan => "Darlehenskonto",
        AccountType.Reserve => "Rücklage",
        _ => "Sonstiges",
    };

    public static string From(BankFormat value) => value switch
    {
        BankFormat.Camt053 => "camt.053",
        BankFormat.Mt940 => "MT940",
        _ => "CSV",
    };

    public static string From(TransactionStatus value) => value switch
    {
        TransactionStatus.Assigned => "zugeordnet",
        TransactionStatus.Ignored => "ignoriert",
        _ => "offen",
    };

    public static string StatusClass(TransactionStatus value) => value switch
    {
        TransactionStatus.Assigned => "status-ok",
        TransactionStatus.Ignored => "",
        _ => "status-wartend",
    };

    public static string From(ChargeType value) => value switch
    {
        ChargeType.Rent => "Miete",
        ChargeType.AdditionalPayment => "Nachzahlung (Abrechnung)",
        ChargeType.Refund => "Erstattung / Gutschrift",
        ChargeType.DunningFee => "Mahngebühr",
        ChargeType.ReturnedDebitFee => "Rücklastschriftgebühr",
        ChargeType.DefaultInterest => "Verzugszinsen",
        ChargeType.Damages => "Schadensersatz",
        ChargeType.Deposit => "Kaution",
        ChargeType.BalanceCarriedForward => "Saldovortrag",
        _ => "Sonstige Forderung",
    };

    public static string From(AllocationOrder value) => value switch
    {
        AllocationOrder.OldestFirst => "Nur nach Fälligkeit (älteste zuerst)",
        AllocationOrder.RentFirst => "Erst Mieten, dann übrige Forderungen",
        _ => "Gesetzlich (§§ 366, 367 BGB): Kosten → Zinsen → älteste Forderung",
    };

    public static string From(DocumentCategory value) => value switch
    {
        DocumentCategory.Lease => "Vertrag",
        DocumentCategory.Minutes => "Protokoll",
        DocumentCategory.Statement => "Abrechnung",
        DocumentCategory.Correspondence => "Korrespondenz",
        DocumentCategory.Invoice => "Rechnung",
        DocumentCategory.Insurance => "Versicherung",
        DocumentCategory.EnergyCertificate => "Energieausweis",
        DocumentCategory.FloorPlan => "Grundriss",
        DocumentCategory.Photo => "Foto",
        DocumentCategory.HouseRules => "Hausordnung",
        _ => "Sonstiges",
    };

    public static string From(HeatingType value) => value switch
    {
        HeatingType.CentralHeating => "Zentralheizung",
        HeatingType.FloorHeating => "Etagenheizung",
        HeatingType.DistrictHeating => "Fernwärme",
        HeatingType.HeatPump => "Wärmepumpe",
        HeatingType.IndividualStoves => "Einzelöfen",
        HeatingType.Other => "Sonstige",
        _ => "unbekannt",
    };

    public static string From(EnergySource value) => value switch
    {
        EnergySource.NaturalGas => "Erdgas",
        EnergySource.HeatingOil => "Heizöl",
        EnergySource.DistrictHeating => "Fernwärme",
        EnergySource.Electricity => "Strom",
        EnergySource.Wood => "Holz/Pellets",
        EnergySource.LiquefiedGas => "Flüssiggas",
        EnergySource.Other => "Sonstige",
        _ => "unbekannt",
    };

    public static string From(UnitType value) => value switch
    {
        UnitType.Apartment => "Wohnung",
        UnitType.Commercial => "Gewerbe",
        UnitType.ParkingSpace => "Stellplatz",
        _ => "Sonstiges",
    };

    public static string From(Furnishing value) => value switch
    {
        Furnishing.PartlyFurnished => "teilmöbliert",
        Furnishing.Furnished => "möbliert",
        _ => "unmöbliert",
    };

    public static string From(Salutation value) => value switch
    {
        Salutation.Mrs => "Frau",
        Salutation.Mr => "Herr",
        Salutation.Diverse => "divers",
        _ => "–",
    };

    public static string From(ContactType value) => value switch
    {
        ContactType.Caretaker => "Hausmeister",
        ContactType.Tradesperson => "Handwerker",
        ContactType.Utility => "Versorger",
        ContactType.Insurance => "Versicherung",
        ContactType.Authority => "Behörde",
        ContactType.ServiceProvider => "Dienstleister",
        ContactType.Other => "Sonstiger Kontakt",
        _ => "privat (Mieter, Bürge …)",
    };

    public static string From(RentComponentType value) => value switch
    {
        RentComponentType.BaseRent => "Kaltmiete",
        RentComponentType.OperatingCostPrepayment => "Betriebskosten-Vorauszahlung",
        RentComponentType.HeatingCostPrepayment => "Heizkosten-Vorauszahlung",
        RentComponentType.OperatingCostFlatRate => "Betriebskostenpauschale",
        RentComponentType.ParkingSpace => "Stellplatz",
        RentComponentType.FurnishingSurcharge => "Möblierungszuschlag",
        _ => "Sonstiges",
    };

    public static string From(BillingType value) => value switch
    {
        BillingType.FlatRate => "Pauschale (keine Abrechnung)",
        BillingType.Inclusive => "Inklusivmiete",
        _ => "Vorauszahlung mit Abrechnung",
    };

    public static string From(PaymentMethod value) => value == PaymentMethod.DirectDebit ? "Lastschrift" : "Überweisung";

    public static string From(TerminatingParty value) => value switch
    {
        TerminatingParty.Landlord => "Vermieter",
        TerminatingParty.Mutual => "einvernehmlich",
        _ => "Mieter",
    };

    public static string From(PartyRole value) => value == PartyRole.Guarantor ? "Bürge" : "Mieter";

    public static string From(LeaseStatus value) => value switch
    {
        LeaseStatus.Future => "beginnt künftig",
        LeaseStatus.Terminated => "gekündigt/befristet",
        LeaseStatus.Ended => "beendet",
        _ => "läuft",
    };

    public static string StatusClass(LeaseStatus value) => value switch
    {
        LeaseStatus.Active => "status-ok",
        LeaseStatus.Terminated or LeaseStatus.Future => "status-wartend",
        _ => "",
    };

    public static string From(RentModel value) => value switch
    {
        RentModel.Graduated => "Staffelmiete",
        RentModel.Index => "Indexmiete",
        _ => "Standardmiete",
    };

    public static string From(MeterType value) => value switch
    {
        MeterType.HotWater => "Warmwasser",
        MeterType.Heat => "Wärme",
        MeterType.Electricity => "Strom",
        MeterType.Gas => "Gas",
        MeterType.OilTank => "Öltank",
        _ => "Kaltwasser",
    };

    public static string From(MeasurementUnit value) => value switch
    {
        MeasurementUnit.KilowattHours => "kWh",
        MeasurementUnit.MegawattHours => "MWh",
        MeasurementUnit.Liters => "l",
        _ => "m³",
    };

    public static string From(ReadingType value) => value switch
    {
        ReadingType.Estimate => "Schätzung",
        ReadingType.InterimReading => "Zwischenablesung",
        ReadingType.Installation => "Einbau",
        ReadingType.Removal => "Ausbau",
        _ => "Ablesung",
    };
}
