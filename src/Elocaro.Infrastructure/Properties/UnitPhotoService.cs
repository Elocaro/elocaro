using Elocaro.Application.Common;
using Elocaro.Application.Properties;
using Elocaro.Domain.Common;
using Elocaro.Domain.Files;
using Elocaro.Domain.Properties;
using Elocaro.Infrastructure.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Properties;

internal sealed class UnitPhotoService(ElocaroDbContext db) : IUnitPhotos
{
    public async Task<IReadOnlyList<UnitPhotoRow>> ListAsync(Guid rentalUnitId, CancellationToken cancellationToken = default) =>
        [.. (await db.Set<UnitPhoto>().AsNoTracking().Where(p => p.RentalUnitId == rentalUnitId).OrderBy(p => p.Order).ThenBy(p => p.CreatedAt).ToListAsync(cancellationToken))
            .Select(p => new UnitPhotoRow(p.Id, p.FileId, p.Caption, p.Order))];

    public Task<Result> AddAsync(Guid rentalUnitId, Guid fileId, string? caption, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            if (!await db.Set<Unit>().AnyAsync(u => u.Id == rentalUnitId, cancellationToken) && !await db.Set<Room>().AnyAsync(r => r.Id == rentalUnitId, cancellationToken))
            {
                throw new NotFoundException();
            }

            if (!await db.Set<StoredFile>().AnyAsync(f => f.Id == fileId && f.MimeType.StartsWith("image/"), cancellationToken))
            {
                throw new RuleViolationException("Das Foto muss ein Bild sein.");
            }

            var existing = await db.Set<UnitPhoto>().Where(p => p.RentalUnitId == rentalUnitId).Select(p => p.Order).ToListAsync(cancellationToken);
            if (existing.Count >= UnitPhoto.MaxPerUnit)
            {
                throw new RuleViolationException($"Höchstens {UnitPhoto.MaxPerUnit} Fotos je Wohnung oder Zimmer.");
            }

            var photo = new UnitPhoto(rentalUnitId, fileId, caption, existing.Count == 0 ? 1 : existing.Max() + 1);
            db.Add(photo);
            await db.SaveChangesAsync(cancellationToken);
            return photo.Id;
        });

    public Task<Result> SetCaptionAsync(Guid id, string? caption, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var photo = await db.Set<UnitPhoto>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            photo.SetCaption(caption);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> MoveAsync(Guid id, bool up, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var photo = await db.Set<UnitPhoto>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            var all = await db.Set<UnitPhoto>().Where(p => p.RentalUnitId == photo.RentalUnitId).OrderBy(p => p.Order).ThenBy(p => p.CreatedAt).ToListAsync(cancellationToken);
            var index = all.IndexOf(all.Single(p => p.Id == id));
            var other = index + (up ? -1 : 1);
            if (other >= 0 && other < all.Count)
            {
                (all[index], all[other]) = (all[other], all[index]);
                for (var i = 0; i < all.Count; i++)
                {
                    all[i].MoveTo(i + 1);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            return id;
        });

    public Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var photo = await db.Set<UnitPhoto>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            db.Remove(photo);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });
}
