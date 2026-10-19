using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace MietHahn.Web.Components.Pages.Admin.Receipts;

/// <summary>
/// Carries the ids of files uploaded in this flow from one form to the next, signed and encrypted. This way a posted form
/// cannot name arbitrary stored files (they would otherwise be attached to the receipt or sent to an extraction provider).
/// </summary>
internal static class UploadToken
{
    private const string Purpose = "MietHahn.ReceiptUpload";

    public static string Protect(IDataProtectionProvider provider, IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(ids);
        return provider.CreateProtector(Purpose).Protect(string.Join(',', ids.Select(i => i.ToString("N", CultureInfo.InvariantCulture))));
    }

    /// <summary>Empty list for missing, forged or foreign tokens.</summary>
    public static IReadOnlyList<Guid> Unprotect(IDataProtectionProvider provider, string? token)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(token))
        {
            return [];
        }

        try
        {
            return provider.CreateProtector(Purpose).Unprotect(token)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Guid.TryParseExact(t, "N", out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return [];
        }
    }
}
