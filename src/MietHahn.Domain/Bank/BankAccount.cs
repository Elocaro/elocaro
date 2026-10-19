using MietHahn.Domain.Common;

namespace MietHahn.Domain.Bank;

public enum AccountType
{
    RentAccount = 0,
    Loan = 1,
    Reserve = 2,
    Miscellaneous = 9,
}

/// <summary>Bankkonto des Vermieters, optional bestimmten Objekten zugeordnet.</summary>
public sealed class BankAccount : Entity
{
    private BankAccount()
    {
        Name = "";
        Iban = "";
    }

    public BankAccount(string name, string iban, AccountType type = AccountType.RentAccount)
    {
        Name = name;
        Iban = "";
        SetIban(iban);
        Type = type;
        Validate();
    }

    public string Name { get; set; }

    /// <summary>Kompakt ohne Leerzeichen, in Großbuchstaben.</summary>
    [Confidential]
    public string Iban { get; private set; }

    public string? Bic { get; set; }

    public string? Bank { get; set; }

    public AccountType Type { get; set; }

    public List<Guid> PropertyIds { get; private set; } = [];

    public void SetIban(string iban)
    {
        if (!Common.Iban.IsValid(iban))
        {
            throw new RuleViolationException("Die IBAN ist ungültig (Prüfsumme).");
        }

        Iban = Common.Iban.Compact(iban);
    }

    public void SetProperties(IEnumerable<Guid> propertyIds)
    {
        ArgumentNullException.ThrowIfNull(propertyIds);
        PropertyIds = propertyIds.Distinct().ToList();
    }

    public void Validate() => ArgumentException.ThrowIfNullOrWhiteSpace(Name);
}

public enum TransactionStatus
{
    Open = 0,
    Assigned = 1,
    Ignored = 2,
}

/// <summary>Ein Kontoumsatz aus dem Bankimport; Betrag positiv = Eingang.</summary>
public sealed class Transaction : Entity
{
    private Transaction()
    {
        Hash = "";
        Purpose = "";
    }

    public Transaction(Guid bankAccountId, Guid? bankImportId, ParsedTransaction read, string hash)
    {
        ArgumentNullException.ThrowIfNull(read);
        BankAccountId = bankAccountId;
        BankImportId = bankImportId;
        BookingDate = read.BookingDate;
        ValueDate = read.ValueDate;
        Amount = read.Amount;
        Currency = read.Currency;
        CounterpartyName = read.CounterpartyName;
        CounterpartyIban = read.CounterpartyIban;
        CounterpartyBic = read.CounterpartyBic;
        Purpose = read.Purpose;
        BookingText = read.BookingText;
        EndToEndId = read.EndToEndId;
        MandateReference = read.MandateReference;
        CreditorId = read.CreditorId;
        Hash = hash;
    }

    public Guid BankAccountId { get; private set; }

    public Guid? BankImportId { get; private set; }

    public DateOnly BookingDate { get; private set; }

    public DateOnly? ValueDate { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public string? CounterpartyName { get; private set; }

    [Confidential]
    public string? CounterpartyIban { get; private set; }

    public string? CounterpartyBic { get; private set; }

    public string Purpose { get; private set; }

    /// <summary>Art der Buchung laut Bank, z. B. „Gutschrift“, „Lastschrift“.</summary>
    public string? BookingText { get; private set; }

    /// <summary>EREF.</summary>
    public string? EndToEndId { get; private set; }

    /// <summary>MREF.</summary>
    public string? MandateReference { get; private set; }

    /// <summary>CRED.</summary>
    public string? CreditorId { get; private set; }

    public TransactionStatus Status { get; private set; }

    public string? Note { get; set; }

    /// <summary>Fingerabdruck zur Duplikaterkennung, eindeutig je Konto.</summary>
    public string Hash { get; private set; }

    public void Ignore(string? note)
    {
        if (Status == TransactionStatus.Assigned)
        {
            throw new RuleViolationException("Ein zugeordneter Umsatz kann nicht ignoriert werden; erst die Zuordnung lösen.");
        }

        Status = TransactionStatus.Ignored;
        Note = note;
    }

    public void Open()
    {
        if (Status == TransactionStatus.Assigned)
        {
            throw new RuleViolationException("Erst die Zuordnung lösen.");
        }

        Status = TransactionStatus.Open;
    }

    public void MarkAsAssigned() => Status = TransactionStatus.Assigned;
}

/// <summary>Ein aus einer Datei gelesener, noch nicht gespeicherter Umsatz.</summary>
public sealed record ParsedTransaction(
    DateOnly BookingDate,
    decimal Amount,
    string Purpose,
    DateOnly? ValueDate = null,
    string Currency = "EUR",
    string? CounterpartyName = null,
    string? CounterpartyIban = null,
    string? CounterpartyBic = null,
    string? BookingText = null,
    string? EndToEndId = null,
    string? MandateReference = null,
    string? CreditorId = null);

public enum BankFormat
{
    Camt053 = 0,
    Mt940 = 1,
    Csv = 2,
}

public enum ImportStatus
{
    Preview = 0,
    Imported = 1,
}

/// <summary>Hochgeladene Kontoauszugsdatei: erst Vorschau, nach Bestätigung importiert (Importprotokoll).</summary>
public sealed class BankImport : Entity
{
    private BankImport()
    {
        FileName = "";
        Content = [];
    }

    public BankImport(Guid bankAccountId, string fileName, byte[] content, BankFormat format, string? profile)
    {
        BankAccountId = bankAccountId;
        FileName = fileName;
        Content = content;
        Format = format;
        Profile = profile;
    }

    public Guid BankAccountId { get; private set; }

    public string FileName { get; private set; }

    public BankFormat Format { get; private set; }

    /// <summary>Name des CSV-Profils.</summary>
    public string? Profile { get; private set; }

    /// <summary>Nur bis zum Import gespeichert.</summary>
    [NoExport]
    public byte[] Content { get; private set; }

    public ImportStatus Status { get; private set; }

    public int Read { get; private set; }

    public int Added { get; private set; }

    public int Duplicates { get; private set; }

    public DateOnly? From { get; private set; }

    public DateOnly? To { get; private set; }

    public void Complete(int read, int newItem, int duplicates, DateOnly? from, DateOnly? to)
    {
        if (Status == ImportStatus.Imported)
        {
            throw new RuleViolationException("Diese Datei wurde bereits importiert.");
        }

        Status = ImportStatus.Imported;
        Read = read;
        Added = newItem;
        Duplicates = duplicates;
        From = from;
        To = to;
        Content = [];
    }
}
