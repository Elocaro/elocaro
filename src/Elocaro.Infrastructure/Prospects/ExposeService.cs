using Elocaro.Application.Common;
using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Application.Properties;
using Elocaro.Application.Prospects;
using Elocaro.Domain.Prospects;
using Elocaro.Infrastructure.Templates;

namespace Elocaro.Infrastructure.Prospects;

internal sealed class ExposeService(IPropertyService properties, IPdfGenerator pdf, IFileStore files) : IExposes
{
    public async Task<NewLetCheck?> CheckAsync(Guid unitId, ExposeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await properties.GetUnitAsync(unitId, cancellationToken) is not { } unit || await properties.GetAsync(unit.PropertyId, cancellationToken) is not { } property)
        {
            return null;
        }

        return NewLetRent.Check(input.Rent ?? 0m, unit.Area?.Area, property.Data.RentIndexPerSquareMetre, input.PreviousRent, property.Data.RentCapPercent);
    }

    public async Task<ExposeFile> CreateAsync(Guid unitId, ExposeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Rent is not > 0m)
        {
            return new ExposeFile(Result.Failed("Bitte die Kaltmiete angeben."));
        }

        if (await properties.GetUnitAsync(unitId, cancellationToken) is not { } unit || await properties.GetAsync(unit.PropertyId, cancellationToken) is not { } property)
        {
            return new ExposeFile(Result.Failed("Die Einheit wurde nicht gefunden."));
        }

        var data = unit.Data;
        List<string> features = [];
        if (data.Balcony)
        {
            features.Add("Balkon oder Terrasse");
        }

        if (data.FittedKitchen)
        {
            features.Add("Einbauküche");
        }

        if (!string.IsNullOrWhiteSpace(data.Bathroom))
        {
            features.Add($"Bad: {data.Bathroom.Trim()}");
        }

        features.AddRange((data.Amenities ?? "").Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var total = input.Rent.Value + (input.OperatingCosts ?? 0m) + (input.HeatingCosts ?? 0m);
        var model = new
        {
            unit_name = data.Name,
            address = property.Address,
            location = Blank(data.Location),
            photo = await PhotoAsync(property.ImageFileId, cancellationToken),
            area = unit.Area?.Area,
            room_count = data.RoomCount,
            floor = data.Floor,
            year_built = property.Data.YearBuilt,
            energy_standard = Blank(data.EnergyStandard),
            available_from = input.AvailableFrom,
            features,
            rent = input.Rent,
            operating_costs = Positive(input.OperatingCosts),
            heating_costs = Positive(input.HeatingCosts),
            total,
            deposit = Positive(input.Deposit),
            previous_rent = input.PreviousRent,
            paragraphs = (input.Description ?? "").Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        };

        var bytes = await pdf.GenerateAsync(new PdfRequest("expose", $"Exposé {data.Name}, {property.Address}", model, PropertyId: property.Id), cancellationToken);
        return new ExposeFile(Result.Ok(unitId), bytes, $"Expose-{Safe(property.Data.Name)}-{Safe(data.Name)}.pdf");
    }

    private async Task<RawHtml?> PhotoAsync(Guid? fileId, CancellationToken cancellationToken)
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
            return new RawHtml($"<img src=\"data:{opened.StoredFile.MimeType};base64,{Convert.ToBase64String(buffer.ToArray())}\" style=\"max-height: 80mm; max-width: 170mm\" alt=\"Foto des Hauses\">");
        }
    }

    private static decimal? Positive(decimal? value) => value is > 0m ? value : null;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Safe(string name) => string.Concat(name.Where(char.IsAsciiLetterOrDigit)) is { Length: > 0 } s ? s : "Einheit";
}
