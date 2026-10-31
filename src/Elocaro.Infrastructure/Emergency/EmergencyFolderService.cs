using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Elocaro.Application.Audit;
using Elocaro.Application.Common;
using Elocaro.Application.Emergency;
using Elocaro.Domain.Admin;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Emergency;

/// <summary>
/// Creates the encrypted emergency folder. The folder is built and encrypted in memory; the password is used once to
/// derive the key and appears in no log, no audit entry and no database column.
/// </summary>
internal sealed class EmergencyFolderService(ElocaroDbContext db, EmergencyFolderContent content, ICurrentUser user, TimeProvider clock) : IEmergencyFolder
{
    private static readonly JsonWriterOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<EmergencyFolderState> StateAsync(CancellationToken cancellationToken = default)
    {
        var stored = await db.Set<EmergencyFolder>().AsNoTracking().OrderBy(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return new EmergencyFolderState(
            new EmergencyFolderNotes { ImportantContacts = stored?.ImportantContacts, DocumentLocation = stored?.DocumentLocation, FurtherNotes = stored?.FurtherNotes },
            stored?.LastCreatedAt);
    }

    public async Task<EmergencyFolderOutcome> SaveNotesAsync(EmergencyFolderNotes notes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (TooLong(notes.ImportantContacts) || TooLong(notes.DocumentLocation) || TooLong(notes.FurtherNotes))
        {
            return new EmergencyFolderOutcome(EmergencyFolderProblem.NotesTooLong);
        }

        var stored = await LoadAsync(cancellationToken);
        Apply(stored, notes);
        await db.SaveChangesAsync(cancellationToken);
        return new EmergencyFolderOutcome(EmergencyFolderProblem.None);
    }

    public async Task<EmergencyFolderResult> CreateAsync(EmergencyFolderRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);
        var now = clock.GetUtcNow();
        var today = GermanTime.Today(now);
        var fileName = $"elocaro-notfallmappe-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.html";

        var password = request.Password ?? "";
        if (password.Length is < EmergencyFolderLimits.MinPasswordLength or > EmergencyFolderLimits.MaxPasswordLength)
        {
            return Failed(EmergencyFolderProblem.PasswordTooShort, fileName);
        }

        if (!string.Equals(password, request.PasswordConfirmation, StringComparison.Ordinal))
        {
            return Failed(EmergencyFolderProblem.PasswordsDiffer, fileName);
        }

        var stored = await LoadAsync(cancellationToken);
        var pages = await content.BuildAsync(stored, request.IncludeLeaseDocuments, principal, cancellationToken);
        if (pages.TooLarge)
        {
            return Failed(EmergencyFolderProblem.TooLarge, fileName);
        }

        byte[] file;
        using (var plaintext = new MemoryStream())
        {
            try
            {
                Write(plaintext, pages);
                file = EmergencyFolderEncryption.Encrypt(plaintext.GetBuffer().AsSpan(0, (int)plaintext.Length), password, today);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext.GetBuffer());
            }
        }

        stored.LastCreatedAt = now;
        // Independent of Audit:LogAccess: the folder contains personal data of all tenants. The password is not part of the entry.
        db.Add(new AuditEntry(
            now,
            user.Identifier,
            "Notfallmappe",
            Guid.Empty,
            AuditAction.Read,
            "[]",
            request.IncludeLeaseDocuments ? "Notfallmappe erzeugt (verschlüsselt, mit Mietvertragsdokumenten)" : "Notfallmappe erzeugt (verschlüsselt)"));
        await db.SaveChangesAsync(cancellationToken);
        return new EmergencyFolderResult(EmergencyFolderProblem.None, fileName, file);
    }

    private static EmergencyFolderResult Failed(EmergencyFolderProblem problem, string fileName) => new(problem, fileName, null);

    private static bool TooLong(string? text) => text is not null && text.Length > EmergencyFolder.MaxLength;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static void Apply(EmergencyFolder stored, EmergencyFolderNotes notes)
    {
        stored.ImportantContacts = Clean(notes.ImportantContacts);
        stored.DocumentLocation = Clean(notes.DocumentLocation);
        stored.FurtherNotes = Clean(notes.FurtherNotes);
    }

    /// <summary>The single record (tracked); created on first use.</summary>
    private async Task<EmergencyFolder> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await db.Set<EmergencyFolder>().OrderBy(e => e.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            stored = new EmergencyFolder();
            db.Add(stored);
        }

        return stored;
    }

    private static void Write(MemoryStream destination, EmergencyFolderPages pages)
    {
        using var json = new Utf8JsonWriter(destination, JsonOptions);
        json.WriteStartObject();
        json.WriteString("html", pages.Html);
        json.WriteStartArray("files");
        foreach (var file in pages.Files)
        {
            json.WriteStartObject();
            json.WriteString("name", file.Name);
            json.WriteString("type", file.ContentType);
            json.WriteBase64String("data", file.Content);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}
