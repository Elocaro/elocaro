using System.Security.Claims;
using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.Notices;
using Elocaro.Domain.Common;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Notices;
using Elocaro.Domain.People;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Notices;

internal sealed class NoticeService(ElocaroDbContext db, IPdfGenerator pdf, TimeProvider clock) : INotices
{
    private DateOnly Today => GermanTime.Today(clock.GetUtcNow());

    public async Task<IReadOnlyList<NoticeRow>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(user, cancellationToken);
        var notices = await db.Set<Notice>().AsNoTracking().ToListAsync(cancellationToken);
        var rows = await RowsAsync(notices, cancellationToken);
        return rows.OrderByDescending(r => r.IsCurrent).ThenByDescending(r => r.ValidFrom).ToList();
    }

    public async Task<IReadOnlyList<TenantNotice>> CurrentAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
        if (viewer != NoticeViewer.Tenant || TenantScope.UserId(user) is not { } userId)
        {
            return [];
        }

        var today = Today;
        var own = await TenantScope.OwnUnitsAsync(db, persons, today, cancellationToken);
        var properties = own.Select(x => x.Property).Distinct().ToList();
        var notices = (await db.Set<Notice>().AsNoTracking().Where(n => properties.Contains(n.PropertyId)).ToListAsync(cancellationToken))
            .Where(n => n.IsCurrent(today) && own.Any(x => TenantScope.Reaches(n, x.Property, x.Unit))).OrderByDescending(n => n.ValidFrom).ThenByDescending(n => n.CreatedAt).ToList();
        if (notices.Count == 0)
        {
            return [];
        }

        var ids = notices.Select(n => n.Id).ToList();
        var read = (await db.Set<NoticeRead>().AsNoTracking().Where(l => l.UserId == userId && ids.Contains(l.NoticeId)).Select(l => l.NoticeId).ToListAsync(cancellationToken)).ToHashSet();
        var files = (await db.Set<NoticeFile>().AsNoTracking().Where(f => ids.Contains(f.NoticeId)).OrderBy(f => f.CreatedAt).ToListAsync(cancellationToken)).ToLookup(f => f.NoticeId, f => f.FileId);
        var names = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        return notices.Select(n => new TenantNotice(n.Id, n.Title, n.Body, names.GetValueOrDefault(n.PropertyId, ""), n.ValidFrom, n.ValidUntil, n.RequiresAcknowledgement, read.Contains(n.Id), files[n.Id].ToList())).ToList();
    }

    public async Task<NoticeDetails?> GetAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(user, cancellationToken);
        var notice = await db.Set<Notice>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notice is null)
        {
            return null;
        }

        var row = (await RowsAsync([notice], cancellationToken))[0];
        var files = await db.Set<NoticeFile>().AsNoTracking().Where(f => f.NoticeId == id).OrderBy(f => f.CreatedAt).Select(f => f.FileId).ToListAsync(cancellationToken);
        return new NoticeDetails(row, notice.Body, files, (await ReadersAsync([notice], null, cancellationToken))[id]);
    }

    public Task<Result> CreateAsync(ClaimsPrincipal user, NoticeInput input, IReadOnlyList<Guid> fileIds, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(input);
            await RequireAdminAsync(user, cancellationToken);
            var propertyId = input.PropertyId ?? throw new RuleViolationException("Bitte das Objekt wählen.");
            if (!await db.Set<Property>().AnyAsync(o => o.Id == propertyId, cancellationToken))
            {
                throw new RuleViolationException("Das Objekt wurde nicht gefunden.");
            }

            if (fileIds.Count > Notice.MaxAttachments)
            {
                throw new RuleViolationException($"Höchstens {Notice.MaxAttachments} Anhänge je Aushang.");
            }

            var unitIds = input.UnitIds.Distinct().ToList();
            if (unitIds.Count > 0 && await db.Set<Unit>().CountAsync(e => unitIds.Contains(e.Id) && e.PropertyId == propertyId, cancellationToken) != unitIds.Count)
            {
                throw new RuleViolationException("Die gewählten Einheiten gehören nicht zum Objekt.");
            }

            var notice = new Notice(input.Title, input.Body, propertyId, input.ValidFrom ?? Today, input.ValidUntil, input.RequiresAcknowledgement, unitIds);
            db.Add(notice);
            foreach (var fileId in fileIds)
            {
                db.Add(new NoticeFile(notice.Id, fileId));
            }

            await db.SaveChangesAsync(cancellationToken);
            return notice.Id;
        });

    public Task<Result> WithdrawAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            await RequireAdminAsync(user, cancellationToken);
            var notice = await db.Set<Notice>().SingleOrDefaultAsync(n => n.Id == id, cancellationToken) ?? throw new NotFoundException();
            notice.Withdraw(Today.AddDays(-1) < notice.ValidFrom ? notice.ValidFrom : Today.AddDays(-1), clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> AcknowledgeAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var (viewer, persons) = await TenantScope.ViewerAsync(db, user, cancellationToken);
            if (viewer != NoticeViewer.Tenant || TenantScope.UserId(user) is not { } userId)
            {
                throw new NotFoundException();
            }

            // Only notices of the own properties count, whatever id the caller passes.
            var own = await TenantScope.OwnUnitsAsync(db, persons, Today, cancellationToken);
            var candidate = await db.Set<Notice>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
            var notice = candidate is not null && own.Any(x => TenantScope.Reaches(candidate, x.Property, x.Unit)) ? candidate : throw new NotFoundException();
            if (!await db.Set<NoticeRead>().AnyAsync(l => l.NoticeId == id && l.UserId == userId, cancellationToken))
            {
                db.Add(new NoticeRead(notice.Id, userId, clock.GetUtcNow()));
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Parallel request has already stored it (unique index): fine.
                }
            }

            return id;
        });

    public async Task<byte[]?> PrintAsync(ClaimsPrincipal user, Guid id, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(user, cancellationToken);
        var notice = await db.Set<Notice>().AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notice is null)
        {
            return null;
        }

        var property = await db.Set<Property>().AsNoTracking().Where(o => o.Id == notice.PropertyId).Select(o => o.Name).FirstOrDefaultAsync(cancellationToken) ?? "";
        var paragraphs = notice.Body.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        return await pdf.GenerateAsync(new PdfRequest("notice", notice.Title, PropertyId: notice.PropertyId, Model: new
        {
            title = notice.Title,
            paragraphs,
            property,
            valid_from = notice.ValidFrom,
            valid_until = notice.ValidUntil,
        }), cancellationToken);
    }

    public async Task<IReadOnlyList<LeaseNotice>> ForLeaseAsync(ClaimsPrincipal user, Guid leaseId, CancellationToken cancellationToken = default)
    {
        await RequireAdminAsync(user, cancellationToken);
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken);
        if (lease is null)
        {
            return [];
        }

        var propertyId = (await db.Set<Unit>().AsNoTracking().Where(e => e.Id == lease.UnitId).Select(e => (Guid?)e.PropertyId).FirstOrDefaultAsync(cancellationToken)) ?? Guid.Empty;
        var notices = (await db.Set<Notice>().AsNoTracking().Where(n => n.PropertyId == propertyId).ToListAsync(cancellationToken))
            .Where(n => n.AppliesToUnit(lease.UnitId) && n.ValidFrom <= (lease.End ?? DateOnly.MaxValue) && (n.ValidUntil ?? DateOnly.MaxValue) >= lease.Start)
            .OrderByDescending(n => n.ValidFrom).ToList();
        var readers = await ReadersAsync(notices, lease, cancellationToken);
        return notices.Select(n => new LeaseNotice(n.Id, n.Title, n.ValidFrom, n.ValidUntil, n.RequiresAcknowledgement, readers[n.Id])).ToList();
    }

    private async Task RequireAdminAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if ((await TenantScope.ViewerAsync(db, user, cancellationToken)).Viewer != NoticeViewer.Admin)
        {
            throw new RuleViolationException("Dafür fehlt die Berechtigung.");
        }
    }

    /// <summary>Tenants with a portal account of the property's current leases (or only of <paramref name="lease"/>).</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<NoticeReader>>> ReadersAsync(List<Notice> notices, Lease? lease, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<NoticeReader>>();
        if (notices.Count == 0)
        {
            return result;
        }

        var leases = lease is null ? await TenantScope.CurrentLeasesAsync(db, Today, cancellationToken) : [lease];
        var units = await TenantScope.PropertyOfUnitAsync(db, cancellationToken);
        var ids = notices.Select(n => n.Id).ToList();
        var reads = (await db.Set<NoticeRead>().AsNoTracking().Where(l => ids.Contains(l.NoticeId)).ToListAsync(cancellationToken)).ToLookup(l => l.NoticeId);
        var personIds = leases.SelectMany(v => v.Tenant.Select(p => p.PersonId)).Distinct().ToList();
        var people = await db.Set<Person>().AsNoTracking().Where(p => personIds.Contains(p.Id) && p.UserId != null && p.AnonymizedAt == null).ToDictionaryAsync(p => p.Id, cancellationToken);
        foreach (var notice in notices)
        {
            var tenants = leases.Where(v => lease is not null || (units.GetValueOrDefault(v.UnitId) == notice.PropertyId && notice.AppliesToUnit(v.UnitId)))
                .SelectMany(v => v.Tenant.Select(p => p.PersonId)).Distinct()
                .Select(p => people.GetValueOrDefault(p)).OfType<Person>().OrderBy(p => p.LastName).ToList();
            result[notice.Id] = tenants.Select(p => new NoticeReader(p.DisplayName, reads[notice.Id].FirstOrDefault(l => l.UserId == p.UserId)?.ReadAt)).ToList();
        }

        return result;
    }

    private async Task<List<NoticeRow>> RowsAsync(List<Notice> notices, CancellationToken cancellationToken)
    {
        var names = await db.Set<Property>().AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, cancellationToken);
        var unitNames = await db.Set<Unit>().AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);
        var readers = await ReadersAsync(notices, null, cancellationToken);
        var today = Today;
        return notices.Select(n => new NoticeRow(n.Id, n.Title, names.GetValueOrDefault(n.PropertyId, ""), n.ValidFrom, n.ValidUntil, n.IsCurrent(today), n.RequiresAcknowledgement,
            readers[n.Id].Count, readers[n.Id].Count(r => r.ReadAt is not null),
            n.UnitIds.Count == 0 ? "ganzes Haus" : string.Join(", ", n.UnitIds.Select(u => unitNames.GetValueOrDefault(u, "Einheit")).Order(StringComparer.CurrentCulture)))).ToList();
    }
}
