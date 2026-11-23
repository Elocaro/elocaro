using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.Properties;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Leases;
using Elocaro.Domain.Properties;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Persistence;
using Elocaro.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Prospects;

internal sealed class ExposeService(IPropertyService properties, IUnitPhotos photos, IPdfGenerator pdf, IFileStore files, ElocaroDbContext db) : IExposes
{
    /// <summary>What the exposé is about: a unit or one room of a shared flat (then the photos of the flat follow those of the room).</summary>
    private sealed record Subject(PropertyDetails Property, string Name, string? Location, decimal? Area, decimal? RoomCount, int? Floor, string? EnergyStandard, IReadOnlyList<string> Features, IReadOnlyList<Guid> PhotoOwners);

    public async Task<NewLetCheck?> CheckAsync(Guid rentalUnitId, ExposeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await FindAsync(rentalUnitId, cancellationToken) is not { } s)
        {
            return null;
        }

        return NewLetRent.Check(input.Rent ?? 0m, s.Area, s.Property.Data.RentIndexPerSquareMetre, input.PreviousRent, s.Property.Data.RentCapPercent);
    }

    public async Task<NewLetCheck?> CheckLettingAsync(Guid rentalUnitId, decimal rent, CancellationToken cancellationToken = default)
    {
        if (await FindAsync(rentalUnitId, cancellationToken) is not { } s)
        {
            return null;
        }

        var last = (await db.Set<Lease>().AsNoTracking().Include(l => l.Components).Where(l => l.RentalUnitId == rentalUnitId && l.Term.To != null).ToListAsync(cancellationToken)).MaxBy(l => l.End);
        var previousRent = last?.AmountAt(RentComponentType.BaseRent, last.End!.Value);
        return NewLetRent.Check(rent, s.Area, s.Property.Data.RentIndexPerSquareMetre, previousRent, s.Property.Data.RentCapPercent);
    }

    public async Task<ExposeFile> CreateAsync(Guid rentalUnitId, ExposeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Rent is not > 0m)
        {
            return new ExposeFile(Result.Failed("Bitte die Kaltmiete angeben."));
        }

        if (await FindAsync(rentalUnitId, cancellationToken) is not { } s)
        {
            return new ExposeFile(Result.Failed("Die Einheit wurde nicht gefunden."));
        }

        var pictures = new List<object>();
        foreach (var owner in s.PhotoOwners)
        {
            foreach (var photo in await photos.ListAsync(owner, cancellationToken))
            {
                if (await ImageAsync(photo.FileId, "max-width: 82mm; max-height: 60mm", cancellationToken) is { } image)
                {
                    pictures.Add(new { image, caption = photo.Caption });
                }
            }
        }

        var total = input.Rent.Value + (input.OperatingCosts ?? 0m) + (input.HeatingCosts ?? 0m);
        var model = new
        {
            unit_name = s.Name,
            address = s.Property.Address,
            location = Blank(s.Location),
            photo = await ImageAsync(s.Property.ImageFileId, "max-height: 80mm; max-width: 170mm", cancellationToken),
            area = s.Area,
            room_count = s.RoomCount,
            floor = s.Floor,
            year_built = s.Property.Data.YearBuilt,
            energy_standard = Blank(s.EnergyStandard),
            available_from = input.AvailableFrom,
            features = s.Features,
            pictures,
            rent = input.Rent,
            operating_costs = Positive(input.OperatingCosts),
            heating_costs = Positive(input.HeatingCosts),
            total,
            deposit = Positive(input.Deposit),
            previous_rent = input.PreviousRent,
            paragraphs = (input.Description ?? "").Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        };

        var bytes = await pdf.GenerateAsync(new PdfRequest("expose", $"Exposé {s.Name}, {s.Property.Address}", model, PropertyId: s.Property.Id), cancellationToken);
        return new ExposeFile(Result.Ok(rentalUnitId), bytes, $"Expose-{Safe(s.Property.Data.Name)}-{Safe(s.Name)}.pdf");
    }

    private async Task<Subject?> FindAsync(Guid rentalUnitId, CancellationToken cancellationToken)
    {
        if (await properties.GetUnitAsync(rentalUnitId, cancellationToken) is { } unit)
        {
            if (await properties.GetAsync(unit.PropertyId, cancellationToken) is not { } property)
            {
                return null;
            }

            var d = unit.Data;
            List<string> features = [];
            if (d.Balcony)
            {
                features.Add("Balkon oder Terrasse");
            }

            if (d.FittedKitchen)
            {
                features.Add("Einbauküche");
            }

            if (!string.IsNullOrWhiteSpace(d.Bathroom))
            {
                features.Add($"Bad: {d.Bathroom.Trim()}");
            }

            features.AddRange(Lines(d.Amenities));
            return new Subject(property, d.Name, d.Location, unit.Area?.Area, d.RoomCount, d.Floor, d.EnergyStandard, features, [rentalUnitId]);
        }

        if (await properties.GetRoomAsync(rentalUnitId, cancellationToken) is { } room && await properties.GetAsync(room.PropertyId, cancellationToken) is { } roomProperty)
        {
            List<string> features = [];
            if (room.Data.Furnishing != Furnishing.Unfurnished)
            {
                features.Add(room.Data.Furnishing == Furnishing.Furnished ? "Möbliert" : "Teilmöbliert");
            }

            features.AddRange(Lines(room.Data.FurnishingList));
            return new Subject(roomProperty, room.Data.Name, $"WG-Zimmer in {room.UnitName}", room.Area?.Area, null, null, null, features, [rentalUnitId, room.UnitId]);
        }

        return null;
    }

    private async Task<RawHtml?> ImageAsync(Guid? fileId, string style, CancellationToken cancellationToken)
    {
        if (fileId is not { } id || await files.OpenAsync(id, cancellationToken) is not { } opened)
        {
            return null;
        }

        await using (opened)
        {
            if (!opened.StoredFile.IsImage)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            await opened.Content.CopyToAsync(buffer, cancellationToken);
            return new RawHtml($"<img src=\"data:{opened.StoredFile.MimeType};base64,{Convert.ToBase64String(buffer.ToArray())}\" style=\"{style}\" alt=\"\">");
        }
    }

    private static string[] Lines(string? text) => (text ?? "").Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static decimal? Positive(decimal? value) => value is > 0m ? value : null;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Safe(string name) => string.Concat(name.Where(char.IsAsciiLetterOrDigit)) is { Length: > 0 } s ? s : "Einheit";
}
