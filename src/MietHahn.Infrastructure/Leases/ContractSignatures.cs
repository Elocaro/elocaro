using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Common;
using MietHahn.Application.Communication;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Domain.Common;
using MietHahn.Domain.Leases;
using MietHahn.Domain.People;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Properties;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Leases;

internal sealed class ContractSignatures(
    MietHahnDbContext db,
    IPdfGenerator pdf,
    IFileStore files,
    IMailSender mail,
    ILeaseContracts contracts,
    IOptions<ApplicationOptions> app,
    TimeProvider clock) : IContractSignatures
{
    private static readonly TimeSpan Validity = TimeSpan.FromDays(14);
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private const int MaxSignatureBytes = 400 * 1024;

    public async Task<IReadOnlyList<SignatureRow>> ListAsync(Guid contractId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        return (await db.Set<SignatureRequest>().AsNoTracking().Where(s => s.ContractId == contractId).ToListAsync(cancellationToken)).OrderBy(s => s.Role).ThenBy(s => s.CreatedAt)
            .Select(s => new SignatureRow(s.Id, s.Role, s.SignerName, s.Email, s.SentAt, s.ExpiresAt, s.SignedAt, s.IsExpired(now), s.DocumentHash)).ToList();
    }

    public async Task<SignatureLinks> StartAsync(Guid contractId, bool includeLandlord, CancellationToken cancellationToken = default)
    {
        try
        {
            var contract = await db.Set<LeaseContract>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            if (contract.Status != ContractStatus.Draft)
            {
                throw new RuleViolationException("Nur ein offener Entwurf lässt sich zur Unterschrift senden.");
            }

            if (await db.Set<SignatureRequest>().AnyAsync(s => s.ContractId == contractId, cancellationToken))
            {
                throw new RuleViolationException("Für diesen Entwurf läuft die digitale Unterschrift bereits.");
            }

            var context = await ContextAsync(contract, cancellationToken);
            var requests = new List<(SignatureRequest Request, string Token)>();
            foreach (var tenant in context.Tenants)
            {
                requests.Add(Issue(new SignatureRequest(contractId, SignerRole.Tenant, tenant.DisplayName, tenant.Email, tenant.Id)));
            }

            if (includeLandlord)
            {
                requests.Add(Issue(new SignatureRequest(contractId, SignerRole.Landlord, context.LandlordName, null, null)));
            }

            db.AddRange(requests.Select(r => r.Request));
            await db.SaveChangesAsync(cancellationToken);
            var links = new List<SignatureLink>();
            foreach (var (request, token) in requests)
            {
                links.Add(await SendAsync(request, token, context, cancellationToken));
            }

            await db.SaveChangesAsync(cancellationToken);
            return new SignatureLinks(true, [], links);
        }
        catch (Exception ex) when (ex is RuleViolationException or NotFoundException)
        {
            return new SignatureLinks(false, [ex is NotFoundException ? "Der Vertrag wurde nicht gefunden." : ex.Message], []);
        }
    }

    public async Task<SignatureLinks> ReissueAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = await db.Set<SignatureRequest>().SingleOrDefaultAsync(s => s.Id == requestId, cancellationToken) ?? throw new NotFoundException();
            var contract = await db.Set<LeaseContract>().AsNoTracking().SingleAsync(c => c.Id == request.ContractId, cancellationToken);
            if (contract.Status != ContractStatus.Draft)
            {
                throw new RuleViolationException("Der Vertrag ist nicht mehr offen.");
            }

            var (_, token) = Issue(request);
            var link = await SendAsync(request, token, await ContextAsync(contract, cancellationToken), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new SignatureLinks(true, [], [link]);
        }
        catch (Exception ex) when (ex is RuleViolationException or NotFoundException)
        {
            return new SignatureLinks(false, [ex is NotFoundException ? "Die Anfrage wurde nicht gefunden." : ex.Message], []);
        }
    }

    public Task<Result> CancelAsync(Guid contractId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var requests = await db.Set<SignatureRequest>().Where(s => s.ContractId == contractId).ToListAsync(cancellationToken);
            if (requests.Any(s => s.IsSigned))
            {
                throw new RuleViolationException("Es wurde schon unterschrieben; die Anfrage lässt sich nicht mehr zurückziehen.");
            }

            db.RemoveRange(requests);
            await db.SaveChangesAsync(cancellationToken);
            return contractId;
        });

    public Task<Result> CompleteAsync(Guid contractId, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var contract = await db.Set<LeaseContract>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == contractId, cancellationToken) ?? throw new NotFoundException();
            await FinishAsync(contract, cancellationToken);
            return contractId;
        });

    public async Task<SigningView?> ForSigningAsync(string token, CancellationToken cancellationToken = default)
    {
        var request = await FindAsync(token, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var contract = await db.Set<LeaseContract>().AsNoTracking().SingleAsync(c => c.Id == request.ContractId, cancellationToken);
        var context = await ContextAsync(contract, cancellationToken);
        return new SigningView(request.SignerName, request.Role, context.Title, context.TenantNames, context.RentalUnit, context.Property, request.ExpiresAt,
            request.IsSigned, request.IsExpired(clock.GetUtcNow()), contract.Status == ContractStatus.Draft);
    }

    public async Task<OpenedFile?> OpenDocumentAsync(string token, CancellationToken cancellationToken = default)
    {
        var request = await FindAsync(token, cancellationToken);
        if (request is null || request.IsExpired(clock.GetUtcNow()))
        {
            return null;
        }

        var contract = await db.Set<LeaseContract>().AsNoTracking().SingleAsync(c => c.Id == request.ContractId, cancellationToken);
        return await files.OpenAsync(contract.DraftFileId, cancellationToken);
    }

    public async Task<SigningResult> SignAsync(string token, SigningInput input, SigningEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(evidence);
        try
        {
            var request = await FindTrackedAsync(token, cancellationToken) ?? throw new NotFoundException();
            var contract = await db.Set<LeaseContract>().AsNoTracking().SingleAsync(c => c.Id == request.ContractId, cancellationToken);
            if (contract.Status != ContractStatus.Draft)
            {
                throw new RuleViolationException("Dieser Vertrag ist nicht mehr zur Unterschrift offen.");
            }

            if (!input.Accepted)
            {
                throw new RuleViolationException("Bitte bestätigen Sie, dass Sie den Vertrag gelesen haben.");
            }

            var hash = await HashOfAsync(contract.DraftFileId, cancellationToken);
            Guid? signatureFile = null;
            if (!string.IsNullOrWhiteSpace(input.SignatureData))
            {
                var png = DecodePng(input.SignatureData);
                using var stream = new MemoryStream(png);
                signatureFile = (await files.SaveAsync(stream, "unterschrift.png", cancellationToken)).Id;
            }

            request.Sign(clock.GetUtcNow(), input.TypedName ?? "", signatureFile, hash, evidence.IpAddress, evidence.UserAgent);
            await db.SaveChangesAsync(cancellationToken);

            var all = await db.Set<SignatureRequest>().AsNoTracking().Where(s => s.ContractId == contract.Id).ToListAsync(cancellationToken);
            if (all.Any(s => !s.IsSigned))
            {
                return new SigningResult(true, [], false);
            }

            try
            {
                await FinishAsync(contract, cancellationToken);
                return new SigningResult(true, [], true);
            }
            catch (Exception ex) when (ex is RuleViolationException or HttpRequestException or InvalidOperationException)
            {
                // The signatures are safe; the landlord can file the contract later.
                return new SigningResult(true, [], false, "Ihre Unterschrift wurde gespeichert. Der Vermieter schließt den Vertrag ab.");
            }
        }
        catch (Exception ex) when (ex is RuleViolationException or NotFoundException or FileRejectedException)
        {
            return new SigningResult(false, [ex is NotFoundException ? "Der Link ist ungültig." : ex.Message], false);
        }
    }

    /// <summary>Protocol page, merge with the draft and filing as signed contract.</summary>
    private async Task FinishAsync(LeaseContract contract, CancellationToken cancellationToken)
    {
        var requests = (await db.Set<SignatureRequest>().AsNoTracking().Where(s => s.ContractId == contract.Id).ToListAsync(cancellationToken)).OrderBy(s => s.Role).ThenBy(s => s.CreatedAt).ToList();
        if (requests.Count == 0 || requests.Any(s => !s.IsSigned))
        {
            throw new RuleViolationException("Es haben noch nicht alle unterschrieben.");
        }

        if (contract.Status != ContractStatus.Draft)
        {
            throw new RuleViolationException("Der Vertrag ist bereits abgeschlossen.");
        }

        var hash = await HashOfAsync(contract.DraftFileId, cancellationToken);
        if (requests.Any(s => s.DocumentHash != hash))
        {
            throw new RuleViolationException("Die Prüfsummen der Unterschriften stimmen nicht überein: der Entwurf wurde nach einer Unterschrift verändert.");
        }

        var context = await ContextAsync(contract, cancellationToken);
        await using var draft = await files.OpenAsync(contract.DraftFileId, cancellationToken) ?? throw new RuleViolationException("Der Entwurf wurde nicht gefunden.");
        using var draftBytes = new MemoryStream();
        await draft.Content.CopyToAsync(draftBytes, cancellationToken);

        var signers = new List<object>();
        foreach (var request in requests)
        {
            signers.Add(new
            {
                Role = request.Role == SignerRole.Tenant ? "Mieter" : "Vermieter",
                Name = request.SignerName,
                request.TypedName,
                SignedAt = TimeZoneInfo.ConvertTime(request.SignedAt!.Value, GermanTime.Germany).ToString("dd.MM.yyyy HH:mm 'Uhr'", German),
                IpAddress = request.IpAddress ?? "nicht erfasst",
                Image = request.SignatureFileId is { } id ? await ImageAsync(id, cancellationToken) : null,
            });
        }

        var model = new
        {
            ContractTitle = context.Title,
            context.RentalUnit,
            Tenants = context.TenantNames,
            DocumentName = draft.StoredFile.Name,
            DocumentHash = hash,
            Signers = signers,
        };
        byte[] merged;
        try
        {
            var protocol = await pdf.GenerateAsync(new PdfRequest("signature-protocol", $"Signaturprotokoll zu {context.Title}", model, null, null, GermanTime.Today(clock.GetUtcNow()), context.PropertyId), cancellationToken);
            merged = await pdf.MergeAsync([draftBytes.ToArray(), protocol], cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new RuleViolationException("Der PDF-Dienst ist nicht erreichbar.");
        }

        using var content = new MemoryStream(merged);
        var signedOn = GermanTime.Today(requests.Max(s => s.SignedAt!.Value));
        var result = await contracts.ConcludeAsync(contract.Id, new SignedContractInput { SignedOn = signedOn, ShareWithTenants = true }, "Mietvertrag-digital-unterschrieben.pdf", content, cancellationToken);
        if (!result.Succeeded)
        {
            throw new RuleViolationException(string.Join(' ', result.Errors));
        }
    }

    private (SignatureRequest Request, string Token) Issue(SignatureRequest request)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        request.IssueToken(HashToken(token), clock.GetUtcNow().Add(Validity));
        return (request, token);
    }

    private async Task<SignatureLink> SendAsync(SignatureRequest request, string token, SigningContext context, CancellationToken cancellationToken)
    {
        var url = app.Value.Link($"/unterschrift/{token}").ToString();
        var mailed = false;
        if (request.Email is { } email && request.Role == SignerRole.Tenant)
        {
            var person = context.Tenants.FirstOrDefault(t => t.Id == request.PersonId);
            await mail.EnqueueAsync(new MailRequest(
                [email],
                "signature-request",
                new
                {
                    Salutation = person is null ? "Guten Tag," : LetterParts.SalutationOf([person.Person]),
                    context.Title,
                    Link = url,
                    ExpiresOn = GermanTime.Today(request.ExpiresAt).ToString("dd.MM.yyyy", German),
                    SenderName = context.LandlordName,
                },
                Reference: $"signature:{request.Id}:{request.TokenHash[..8]}"), cancellationToken);
            request.SentAt = clock.GetUtcNow();
            mailed = true;
        }

        return new SignatureLink(request.Id, request.SignerName, request.Role, url, mailed);
    }

    private async Task<SignatureRequest?> FindAsync(string token, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        return await db.Set<SignatureRequest>().AsNoTracking().FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
    }

    private async Task<SignatureRequest?> FindTrackedAsync(string token, CancellationToken cancellationToken)
    {
        var hash = HashToken(token);
        return await db.Set<SignatureRequest>().FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken);
    }

    private async Task<SigningContext> ContextAsync(LeaseContract contract, CancellationToken cancellationToken)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleAsync(v => v.Id == contract.LeaseId, cancellationToken);
        var ids = lease.Tenant.Select(p => p.PersonId).ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var tenants = ids.Select(i => people.First(p => p.Id == i)).Select(p => new Signer(p.Id, p.DisplayName, p.Email, p)).ToList();
        var unit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId);
        var landlord = await SenderProfiles.ForUnitAsync(db, lease.UnitId, cancellationToken);
        var draftName = await db.Set<Domain.Files.StoredFile>().AsNoTracking().Where(f => f.Id == contract.DraftFileId).Select(f => f.Name).SingleAsync(cancellationToken);
        var title = Path.GetFileNameWithoutExtension(draftName);
        var tenantNames = string.Join(" und ", tenants.Select(t => t.DisplayName));
        return new SigningContext(string.IsNullOrWhiteSpace(title) ? "Mietvertrag" : title, tenantNames, unit?.Name ?? "", unit?.Property ?? "", unit?.PropertyId, landlord?.Name ?? "Ihre Hausverwaltung", tenants);
    }

    private async Task<string> HashOfAsync(Guid fileId, CancellationToken cancellationToken)
    {
        await using var opened = await files.OpenAsync(fileId, cancellationToken) ?? throw new RuleViolationException("Der Entwurf wurde nicht gefunden.");
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(opened.Content, cancellationToken));
    }

    private async Task<RawHtml> ImageAsync(Guid fileId, CancellationToken cancellationToken)
    {
        await using var opened = await files.OpenAsync(fileId, cancellationToken);
        if (opened is null)
        {
            return new RawHtml("");
        }

        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, cancellationToken);
        return new RawHtml($"<img src=\"data:{opened.StoredFile.MimeType};base64,{Convert.ToBase64String(buffer.ToArray())}\" style=\"max-height: 18mm; max-width: 80mm\" alt=\"Unterschrift\">");
    }

    private static byte[] DecodePng(string data)
    {
        var text = data.Contains(',', StringComparison.Ordinal) ? data[(data.IndexOf(',', StringComparison.Ordinal) + 1)..] : data;
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw new RuleViolationException("Die Unterschrift konnte nicht gelesen werden. Bitte zeichnen Sie sie neu oder geben Sie nur den Namen ein.");
        }

        if (bytes.Length > MaxSignatureBytes || bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
        {
            throw new RuleViolationException("Die Unterschrift konnte nicht gelesen werden. Bitte zeichnen Sie sie neu oder geben Sie nur den Namen ein.");
        }

        return bytes;
    }

    private static string HashToken(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? "")));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record Signer(Guid Id, string DisplayName, string? Email, Person Person);

    private sealed record SigningContext(string Title, string TenantNames, string RentalUnit, string Property, Guid? PropertyId, string LandlordName, List<Signer> Tenants);
}
