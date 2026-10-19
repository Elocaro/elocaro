using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Accounts;
using MietHahn.Application.Bank;
using MietHahn.Application.Common;
using MietHahn.Application.Todos;
using MietHahn.Domain.Bank;
using MietHahn.Domain.Common;
using MietHahn.Domain.Properties;
using MietHahn.Domain.Receipts;
using MietHahn.Infrastructure.Bank.Import;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Bank;

internal sealed class BankService(MietHahnDbContext db) : IBankService
{
    /// <summary>Bank statements are small; larger files are almost certainly something else.</summary>
    internal const int MaxFileSize = 10 * 1024 * 1024;

    public async Task<IReadOnlyList<BankAccountRow>> AccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await db.Set<BankAccount>().AsNoTracking().OrderBy(k => k.Name).ToListAsync(cancellationToken);
        var properties = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var statistics = await db.Set<Transaction>().GroupBy(u => u.BankAccountId)
            .Select(g => new { Id = g.Key, Open = g.Count(u => u.Status == TransactionStatus.Open), Latest = g.Max(u => (DateOnly?)u.BookingDate) })
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        return accounts.Select(k => new BankAccountRow(
            k.Id, k.Name, Iban.Format(k.Iban), k.Bank, k.Type,
            k.PropertyIds.Select(o => properties.GetValueOrDefault(o, "?")).Order(StringComparer.CurrentCulture).ToList(),
            statistics.GetValueOrDefault(k.Id)?.Open ?? 0,
            statistics.GetValueOrDefault(k.Id)?.Latest)).ToList();
    }

    public async Task<BankAccountInput?> GetAccountAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await db.Set<BankAccount>().AsNoTracking().SingleOrDefaultAsync(k => k.Id == id, cancellationToken);
        return account is null ? null : new BankAccountInput
        {
            Name = account.Name,
            Iban = Iban.Format(account.Iban),
            Bic = account.Bic,
            Bank = account.Bank,
            Type = account.Type,
            PropertyIds = account.PropertyIds,
        };
    }

    public Task<Result> CreateAccountAsync(BankAccountInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var account = new BankAccount(data.Name.Trim(), data.Iban, data.Type);
            await ApplyAsync(account, data, cancellationToken);
            db.Add(account);
            await SaveAsync(cancellationToken);
            return account.Id;
        });

    public Task<Result> UpdateAccountAsync(Guid id, BankAccountInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var account = await db.Set<BankAccount>().SingleOrDefaultAsync(k => k.Id == id, cancellationToken) ?? throw new NotFoundException();
            var newIban = Iban.Compact(data.Iban);
            if (newIban != account.Iban && await db.Set<Transaction>().AnyAsync(u => u.BankAccountId == id, cancellationToken))
            {
                throw new RuleViolationException("Die IBAN eines Kontos mit Umsätzen lässt sich nicht ändern (Duplikaterkennung); bitte ein neues Konto anlegen.");
            }

            account.SetIban(data.Iban);
            account.Name = data.Name.Trim();
            account.Type = data.Type;
            await ApplyAsync(account, data, cancellationToken);
            await SaveAsync(cancellationToken);
            return id;
        });

    public Task<Result> CreatePreviewAsync(Guid bankAccountId, string fileName, byte[] content, string? profile = null, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(content);
            if (!await db.Set<BankAccount>().AnyAsync(k => k.Id == bankAccountId, cancellationToken))
            {
                throw new NotFoundException();
            }

            if (content.Length > MaxFileSize)
            {
                throw new RuleViolationException("Die Datei ist größer als 10 MB.");
            }

            var read = await ReadAsync(content, string.IsNullOrWhiteSpace(profile) ? null : profile, cancellationToken);
            var import = new BankImport(bankAccountId, Path.GetFileName(fileName), content, read.Format, read.Profile);
            db.Add(import);
            await db.SaveChangesAsync(cancellationToken);
            return import.Id;
        });

    public async Task<ImportPreview?> PreviewAsync(Guid bankImportId, CancellationToken cancellationToken = default)
    {
        var import = await db.Set<BankImport>().AsNoTracking().SingleOrDefaultAsync(i => i.Id == bankImportId, cancellationToken);
        if (import is null)
        {
            return null;
        }

        var account = await db.Set<BankAccount>().AsNoTracking().SingleAsync(k => k.Id == import.BankAccountId, cancellationToken);
        if (import.Status == ImportStatus.Imported)
        {
            return new ImportPreview(import.Id, account.Id, account.Name, import.FileName, import.Format, import.Profile, import.Status, null, [], []);
        }

        var read = await ReadAsync(import.Content, import.Profile, cancellationToken);
        var hashes = TransactionFingerprint.Calculate(account.Iban, read.MoveOut.Transactions);
        var existing = await ExistingAsync(account.Id, hashes, cancellationToken);
        var differing = read.MoveOut.Iban is { } fileIban && Iban.Compact(fileIban) != account.Iban ? Iban.Format(fileIban) : null;
        return new ImportPreview(import.Id, account.Id, account.Name, import.FileName, import.Format, import.Profile, import.Status, differing,
            read.MoveOut.Transactions.Select((u, i) => new PreviewRow(u, existing.Contains(hashes[i]))).ToList(),
            read.MoveOut.Hints);
    }

    public Task<Result> ImportAsync(Guid bankImportId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var import = await db.Set<BankImport>().SingleOrDefaultAsync(i => i.Id == bankImportId, cancellationToken) ?? throw new NotFoundException();
            if (import.Status == ImportStatus.Imported)
            {
                throw new RuleViolationException("Diese Datei wurde bereits importiert.");
            }

            var account = await db.Set<BankAccount>().SingleAsync(k => k.Id == import.BankAccountId, cancellationToken);
            var read = await ReadAsync(import.Content, import.Profile, cancellationToken);
            if (read.MoveOut.Iban is { } fileIban && Iban.Compact(fileIban) != account.Iban)
            {
                throw new RuleViolationException($"Die Datei gehört zum Konto {Iban.Format(fileIban)}, nicht zu {account.Name}.");
            }

            var transactions = read.MoveOut.Transactions;
            var hashes = TransactionFingerprint.Calculate(account.Iban, transactions);
            var existing = await ExistingAsync(account.Id, hashes, cancellationToken);
            var newItem = 0;
            for (var i = 0; i < transactions.Count; i++)
            {
                if (existing.Add(hashes[i]))
                {
                    db.Add(new Transaction(account.Id, import.Id, transactions[i], hashes[i]));
                    newItem++;
                }
            }

            import.Complete(transactions.Count, newItem, transactions.Count - newItem,
                transactions.Count == 0 ? null : transactions.Min(u => u.BookingDate),
                transactions.Count == 0 ? null : transactions.Max(u => u.BookingDate));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
            {
                throw new RuleViolationException("Diese Umsätze werden gerade parallel importiert. Bitte die Seite neu laden.");
            }

            return import.Id;
        });

    public Task<Result> DiscardAsync(Guid bankImportId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var import = await db.Set<BankImport>().SingleOrDefaultAsync(i => i.Id == bankImportId, cancellationToken) ?? throw new NotFoundException();
            if (import.Status == ImportStatus.Imported)
            {
                throw new RuleViolationException("Ein abgeschlossener Import kann nicht verworfen werden.");
            }

            db.Remove(import);
            await db.SaveChangesAsync(cancellationToken);
            return import.BankAccountId;
        });

    public async Task<IReadOnlyList<ImportRow>> ImportsAsync(Guid bankAccountId, CancellationToken cancellationToken = default) =>
        await db.Set<BankImport>().AsNoTracking().Where(i => i.BankAccountId == bankAccountId).OrderByDescending(i => i.CreatedAt)
            .Select(i => new ImportRow(i.Id, i.CreatedAt, i.CreatedBy, i.FileName, i.Format, i.Profile, i.Status, i.Read, i.Added, i.Duplicates, i.From, i.To))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TransactionRow>> TransactionsAsync(TransactionQuery search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = db.Set<Transaction>().AsNoTracking();
        if (search.BankAccountId is { } account)
        {
            query = query.Where(u => u.BankAccountId == account);
        }

        if (search.Status is { } status)
        {
            query = query.Where(u => u.Status == status);
        }

        if (search.WithoutReceipt)
        {
            // Payments whose receipt exists but has no file yet (e.g. created automatically with the assignment).
            var withoutFile = db.Set<TransactionAssignment>()
                .Where(a => a.ReceiptId != null && db.Set<Receipt>().Any(b => b.Id == a.ReceiptId) && !db.Set<ReceiptFile>().Any(d => d.ReceiptId == a.ReceiptId))
                .Select(a => a.TransactionId);
            query = query.Where(u => u.Status == TransactionStatus.Assigned && withoutFile.Contains(u.Id));
        }

        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            var pattern = "%" + search.Text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(u => EF.Functions.ILike(u.Purpose, pattern) || (u.CounterpartyName != null && EF.Functions.ILike(u.CounterpartyName, pattern)) || (u.CounterpartyIban != null && EF.Functions.ILike(u.CounterpartyIban, pattern)));
        }

        return await query.OrderByDescending(u => u.BookingDate).ThenByDescending(u => u.Id)
            .Select(u => new TransactionRow(u.Id, u.BankAccountId, u.BookingDate, u.ValueDate, u.Amount, u.CounterpartyName, u.CounterpartyIban, u.Purpose, u.BookingText,
                u.EndToEndId, u.MandateReference, u.CreditorId, u.Status, u.Note))
            .ToListAsync(cancellationToken);
    }

    public Task<Result> IgnoreAsync(Guid transactionId, string? note, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var transaction = await db.Set<Transaction>().SingleOrDefaultAsync(u => u.Id == transactionId, cancellationToken) ?? throw new NotFoundException();
            transaction.Ignore(string.IsNullOrWhiteSpace(note) ? null : note.Trim());
            await db.SaveChangesAsync(cancellationToken);
            return transaction.Id;
        });

    public Task<Result> ReopenAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var transaction = await db.Set<Transaction>().SingleOrDefaultAsync(u => u.Id == transactionId, cancellationToken) ?? throw new NotFoundException();
            transaction.Open();
            await db.SaveChangesAsync(cancellationToken);
            return transaction.Id;
        });

    public async Task<IReadOnlyList<CsvProfileInput>> CsvProfilesAsync(CancellationToken cancellationToken = default) =>
        (await ProfilesAsync(cancellationToken)).Select(Data).ToList();

    public Task<Result> SaveCsvProfileAsync(Guid? id, CsvProfileInput data, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(data);
            var name = data.Name.Trim();
            if (CsvDefaults.All.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new RuleViolationException("Dieser Name gehört zu einem mitgelieferten Profil.");
            }

            var profile = id is { } existingId
                ? await db.Set<CsvProfile>().SingleOrDefaultAsync(p => p.Id == existingId, cancellationToken) ?? throw new NotFoundException()
                : db.Add(new CsvProfile(name, data.BookingDateColumn.Trim())).Entity;
            profile.Name = name;
            profile.Delimiter = Character(data.Delimiter, ';');
            profile.Charset = data.Charset is "utf-8" or "windows-1252" ? data.Charset : "auto";
            profile.DateFormat = data.DateFormat.Trim();
            profile.DecimalSeparator = Character(data.DecimalSeparator, ',');
            profile.BookingDateColumn = data.BookingDateColumn.Trim();
            profile.ValueDateColumn = TrimToNull(data.ValueDateColumn);
            profile.AmountColumn = TrimToNull(data.AmountColumn);
            profile.DebitColumn = TrimToNull(data.DebitColumn);
            profile.CreditColumn = TrimToNull(data.CreditColumn);
            profile.NameColumn = TrimToNull(data.NameColumn);
            profile.IbanColumn = TrimToNull(data.IbanColumn);
            profile.BicColumn = TrimToNull(data.BicColumn);
            profile.PurposeColumn = TrimToNull(data.PurposeColumn);
            profile.BookingTextColumn = TrimToNull(data.BookingTextColumn);
            profile.EndToEndColumn = TrimToNull(data.EndToEndColumn);
            profile.MandateReferenceColumn = TrimToNull(data.MandateReferenceColumn);
            profile.CreditorIdColumn = TrimToNull(data.CreditorIdColumn);
            profile.Validate();
            if (profile.DateFormat.Split('|').Any(f => f.Trim().Length == 0))
            {
                throw new RuleViolationException("Bitte ein Datumsformat wie „dd.MM.yyyy“ angeben.");
            }

            await SaveAsync(cancellationToken);
            return profile.Id;
        });

    public Task<Result> DeleteCsvProfileAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var profile = await db.Set<CsvProfile>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            db.Remove(profile);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private async Task<ParsedFile> ReadAsync(byte[] content, string? profile, CancellationToken cancellationToken)
    {
        try
        {
            return BankFileReader.Read(content, await ProfilesAsync(cancellationToken), profile);
        }
        catch (BankFileErrorException ex)
        {
            throw new RuleViolationException(ex.Message);
        }
        catch (FormatException ex)
        {
            throw new RuleViolationException("Die Datei enthält ungültige Werte: " + ex.Message);
        }
    }

    private async Task<List<CsvProfile>> ProfilesAsync(CancellationToken cancellationToken) =>
        [.. CsvDefaults.All, .. await db.Set<CsvProfile>().AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken)];

    private async Task<HashSet<string>> ExistingAsync(Guid accountId, IReadOnlyList<string> hashes, CancellationToken cancellationToken)
    {
        var list = hashes.ToList();
        return (await db.Set<Transaction>().IgnoreQueryFilters().Where(u => u.BankAccountId == accountId && list.Contains(u.Hash)).Select(u => u.Hash).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task ApplyAsync(BankAccount account, BankAccountInput data, CancellationToken cancellationToken)
    {
        account.Bic = TrimToNull(data.Bic)?.ToUpperInvariant();
        account.Bank = TrimToNull(data.Bank);
        // Without any checked properties, form binding sets the list to null.
        var propertyIds = (data.PropertyIds ?? []).Distinct().ToList();
        if (await db.Set<Property>().CountAsync(o => propertyIds.Contains(o.Id), cancellationToken) != propertyIds.Count)
        {
            throw new RuleViolationException("Ein gewähltes Objekt wurde nicht gefunden.");
        }

        account.SetProperties(propertyIds);
        account.Validate();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            throw new RuleViolationException("Ein Konto bzw. Profil mit dieser IBAN bzw. diesem Namen gibt es schon.");
        }
    }

    private static CsvProfileInput Data(CsvProfile p) => new()
    {
        Id = p.IsDefault ? null : p.Id,
        IsDefault = p.IsDefault,
        Name = p.Name,
        Delimiter = p.Delimiter.ToString(),
        Charset = p.Charset,
        DateFormat = p.DateFormat,
        DecimalSeparator = p.DecimalSeparator.ToString(),
        BookingDateColumn = p.BookingDateColumn,
        ValueDateColumn = p.ValueDateColumn,
        AmountColumn = p.AmountColumn,
        DebitColumn = p.DebitColumn,
        CreditColumn = p.CreditColumn,
        NameColumn = p.NameColumn ?? p.IncomingNameColumn,
        IbanColumn = p.IbanColumn,
        BicColumn = p.BicColumn,
        PurposeColumn = p.PurposeColumn,
        BookingTextColumn = p.BookingTextColumn,
        EndToEndColumn = p.EndToEndColumn,
        MandateReferenceColumn = p.MandateReferenceColumn,
        CreditorIdColumn = p.CreditorIdColumn,
    };

    private static char Character(string? value, char standard) => value switch
    {
        "\\t" or "Tab" => '\t',
        { Length: 1 } => value[0],
        _ => standard,
    };

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Dashboard: open (unassigned) transactions.</summary>
internal sealed class BankTodos(MietHahnDbContext db) : ITodoSource
{
    public async Task<IReadOnlyList<TodoItem>> TodosAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.IsInRole(Roles.Admin) && !user.HasClaim(Permissions.ClaimType, nameof(AccessRight.Finance)))
        {
            return [];
        }

        var open = await db.Set<Transaction>().CountAsync(u => u.Status == TransactionStatus.Open, cancellationToken);
        return open == 0 ? [] : [new TodoItem("Bank", $"{open} Umsätze ohne Zuordnung", "/verwaltung/bank/zuordnung", Urgency.Soon,
            "Mieteingängen das Mietverhältnis zuordnen, private oder doppelte Umsätze ignorieren.")];
    }
}
