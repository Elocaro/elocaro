using Elocaro.Application.People;
using Elocaro.Domain.Communication;
using Elocaro.Domain.People;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Privacy;

/// <summary>Finds the mails sent to a person, by the address of the person and of their user account.</summary>
internal static class PersonMails
{
    public static async Task<IReadOnlyList<string>> AddressesAsync(ElocaroDbContext db, Guid personId, CancellationToken cancellationToken)
    {
        var person = await db.Set<Person>().AsNoTracking().Where(p => p.Id == personId).Select(p => new { p.Email, p.UserId }).SingleOrDefaultAsync(cancellationToken);
        var account = person?.UserId is { } userId
            ? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new[] { person?.Email, account }.OfType<string>().Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IQueryable<OutgoingMail> To(ElocaroDbContext db, IReadOnlyCollection<string> addresses)
    {
        var lowered = addresses.Select(a => a.ToLowerInvariant()).ToList();
#pragma warning disable CA1304, CA1311, MA0011 // Translated to SQL lower().
        return db.Set<OutgoingMail>().IgnoreQueryFilters().Where(m => m.To.Any(a => lowered.Contains(a.ToLower())));
#pragma warning restore CA1304, CA1311, MA0011
    }
}

/// <summary>Mails to a person for the GDPR access request. The content of mails with a secret link is left out.</summary>
internal sealed class MailPersonalData(ElocaroDbContext db) : IPersonalDataSource
{
    public string Section => "mails";

    public async Task<object?> DataAsync(Guid personId, CancellationToken cancellationToken = default)
    {
        var addresses = await PersonMails.AddressesAsync(db, personId, cancellationToken);
        if (addresses.Count == 0)
        {
            return null;
        }

        var mails = await PersonMails.To(db, addresses).AsNoTracking().OrderBy(m => m.CreatedAt).ToListAsync(cancellationToken);
        return mails.Count == 0
            ? null
            : mails.Select(m => new
            {
                m.To,
                m.Subject,
                Status = m.Status.ToString(),
                m.CreatedAt,
                m.SentAt,
                m.Template,
                Text = m.ContainsSecret ? OutgoingMail.PurgedContent : m.Text,
            }).ToList();
    }
}
