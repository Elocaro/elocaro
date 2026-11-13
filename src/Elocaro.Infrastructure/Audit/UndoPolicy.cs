namespace Elocaro.Infrastructure.Audit;

/// <summary>How a change to an entity type can be taken back (ADR 0007). Every entity type belongs to exactly one class; <c>UndoRulesTests</c> enforces it.</summary>
public enum UndoClass
{
    /// <summary>Master data: creation → soft delete, deletion → restore, change → set the fields back. Enabled.</summary>
    Restorable = 0,

    /// <summary>Restorable in principle, not enabled yet (its dependents or rules have not been looked at).</summary>
    Planned = 1,

    /// <summary>Finance: taking back means a counter entry, never deleting. Not enabled yet.</summary>
    Compensating = 2,

    /// <summary>Cannot be taken back: sent mail, delivered documents, bank and signatures, users and settings of the system.</summary>
    Irreversible = 3,
}

/// <summary>The classification of all entity types by class name (the audit log and the undo work with names).</summary>
public static class UndoPolicy
{
    private static readonly Dictionary<string, UndoClass> Classes = Build();

    public static IReadOnlyDictionary<string, UndoClass> All => Classes;

    public static bool IsRestorable(Type entityType) => IsRestorable(entityType.Name);

    public static bool IsRestorable(string entityType) => Classes.TryGetValue(entityType, out var c) && c == UndoClass.Restorable;

    private static Dictionary<string, UndoClass> Build()
    {
        var result = new Dictionary<string, UndoClass>(StringComparer.Ordinal);
        Add(result, UndoClass.Restorable,
            "Property", "PropertyArea", "PropertyOwner", "PropertyLandlord", "Unit", "UnitArea", "Room", "RoomArea",
            "Person", "PersonAddress", "Lease", "LeaseParty", "LeaseRoom", "RentComponent", "RentStep", "OccupantCount",
            "Meter", "MeterTerm", "MeterReading");
        Add(result, UndoClass.Planned,
            "PropertyAsset", "PropertyCost", "PropertyAcquisition", "PropertyAssignment", "LocalRent", "MaintenanceDistribution", "LandlordProfile",
            "CostAllocationRule", "CostAllocationShare", "CostSubtype", "CostItem", "SupplyContract", "SupplyTariff", "SupplierCostTypeRule",
            "TaxLineMapping", "DatevAccount", "CsvProfile", "MatchRule", "DocumentTemplate", "DocumentTemplateRevision", "Document", "Receipt", "ReceiptFile",
            "RecurringTask", "TaskCompletion", "CareRoutine", "CareCheck", "Loan", "LoanCertificate", "LoanExtraPayment", "FinancingCost",
            "CpiValue", "DegreeDayValue", "Ticket", "TicketComment", "TicketPhoto", "TicketWork", "TicketInvoice", "Notice", "NoticeFile",
            "MessageThread", "Message", "MessageFile", "HeatingSettings", "DeadlineSettings", "FinanceSettings", "ModuleSettings", "DatevSettings");
        Add(result, UndoClass.Compensating,
            "Charge", "Payment", "Transaction", "TransactionAssignment", "DepositEntry", "Dunning", "DunningHold", "BillingStatement", "PrepaymentProposal", "RentIncrease");
        Add(result, UndoClass.Irreversible,
            "OutgoingMail", "StatementDispatch", "ConsumptionInformationDispatch", "SignatureRequest", "PushMessage", "PushSubscription", "PushKeys",
            "BankAccount", "BankDetails", "BankImport", "DatevExport", "DatevExportedItem", "BackupSetting", "GoogleClientSetting", "EmergencyFolder",
            "StoredFile", "DocumentRead", "NoticeRead", "ReadingReminder", "DeadlineReminder", "LegalReferenceCheck", "HandoverProtocol", "ContactChangeRequest",
            "TicketAccessLink", "ReadingReport", "StatementReminderDismissal", "StatementReceiptShare", "LeaseContract");
        return result;
    }

    private static void Add(Dictionary<string, UndoClass> result, UndoClass undoClass, params string[] names)
    {
        foreach (var name in names)
        {
            result.Add(name, undoClass); // a name in two classes throws on start and in the test
        }
    }
}
