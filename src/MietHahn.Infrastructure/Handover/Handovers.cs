using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Application.Leases;
using MietHahn.Application.Metering;
using MietHahn.Domain.Admin;
using MietHahn.Domain.Common;
using MietHahn.Domain.Documents;
using MietHahn.Domain.Leases;
using MietHahn.Domain.Metering;
using MietHahn.Domain.People;
using MietHahn.Domain.Properties;
using MietHahn.Infrastructure.Common;
using MietHahn.Infrastructure.Leases;
using MietHahn.Infrastructure.Persistence;
using MietHahn.Infrastructure.Properties;
using MietHahn.Infrastructure.Templates;

namespace MietHahn.Infrastructure.Handover;

internal sealed class Handovers(MietHahnDbContext db, IPdfGenerator pdf, IFileStore files, IDocumentLibrary documents, IMeterService meters, TimeProvider clock) : IHandovers
{
    private static readonly string[] StandardComponents = ["Wände und Decke", "Boden", "Fenster und Türen", "Ausstattung"];

    private static readonly string[] BathComponents = ["Wände und Decke", "Boden", "Fenster und Türen", "Sanitär (WC, Waschbecken, Wanne/Dusche)", "Armaturen"];

    private static readonly string[] StandardKeys = ["Haustür", "Wohnung", "Briefkasten", "Keller"];

    public async Task<HandoverView?> GetAsync(Guid leaseId, HandoverType type, CancellationToken cancellationToken = default)
    {
        var protocol = await db.Set<HandoverProtocol>().AsNoTracking().SingleOrDefaultAsync(p => p.LeaseId == leaseId && p.Type == type, cancellationToken);
        if (protocol is null)
        {
            return null;
        }

        var context = await LoadAsync(leaseId, cancellationToken);
        IReadOnlyList<HandoverDifference> differences = [];
        if (type == HandoverType.MoveOut && await db.Set<HandoverProtocol>().AsNoTracking().SingleOrDefaultAsync(p => p.LeaseId == leaseId && p.Type == HandoverType.MoveIn, cancellationToken) is { } moveIn)
        {
            differences = HandoverComparison.Compare(moveIn.Content, protocol.Content);
        }

        return new HandoverView(protocol.Id, leaseId, type, protocol.Date, protocol.Status, protocol.Content, $"{context.RentalUnit.Name}, {context.RentalUnit.Property}", context.RentalUnit.Property,
            context.TenantNames, context.Lease.Start, context.Lease.End, protocol.SignedByName, protocol.SignedAt, protocol.DocumentFileId, protocol.DocumentId, differences);
    }

    public Task<Result> CreateAsync(Guid leaseId, HandoverType type, DateOnly date, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            if (await db.Set<HandoverProtocol>().AnyAsync(p => p.LeaseId == leaseId && p.Type == type, cancellationToken))
            {
                throw new RuleViolationException("Für diesen Vertrag gibt es dieses Protokoll schon.");
            }

            var context = await LoadAsync(leaseId, cancellationToken);
            var moveIn = type == HandoverType.MoveOut
                ? await db.Set<HandoverProtocol>().AsNoTracking().SingleOrDefaultAsync(p => p.LeaseId == leaseId && p.Type == HandoverType.MoveIn, cancellationToken)
                : null;
            var content = moveIn is null ? PrefillRooms(context) : TakeOver(moveIn.Content);
            content.Meters = await MetersAsync(context, cancellationToken);
            if (moveIn is null)
            {
                content.Keys = [.. StandardKeys.Select(k => new HandoverKey { Kind = k })];
            }

            var protocol = new HandoverProtocol(leaseId, type, date, content);
            db.Add(protocol);
            await db.SaveChangesAsync(cancellationToken);
            return protocol.Id;
        });

    public Task<Result> SaveAsync(Guid id, DateOnly date, HandoverContent content, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(content);
            var protocol = await db.Set<HandoverProtocol>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            Clean(content);
            protocol.Edit(date, content);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    public Task<Result> AddPhotoAsync(Guid id, string fileName, Stream content, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(content);
            var protocol = await db.Set<HandoverProtocol>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (protocol.Status != HandoverStatus.Draft)
            {
                throw new RuleViolationException("Das Protokoll ist unterschrieben und kann nicht mehr geändert werden.");
            }

            return (await SavePhotoAsync(content, fileName, cancellationToken)).Id;
        });

    public async Task<TemplatePreview> PreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var protocol = await db.Set<HandoverProtocol>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new RuleViolationException("Das Protokoll wurde nicht gefunden.");
            var request = await RequestAsync(protocol, null, null, cancellationToken);
            return new TemplatePreview(await pdf.GenerateAsync(request, cancellationToken), []);
        }
        catch (Exception ex) when (ex is RuleViolationException or InvalidOperationException)
        {
            return new TemplatePreview(null, [ex.Message]);
        }
        catch (HttpRequestException)
        {
            return new TemplatePreview(null, ["Der PDF-Dienst ist nicht erreichbar."]);
        }
    }

    public async Task<HandoverSignResult> SignAsync(Guid id, HandoverSignature signature, bool confirmWarnings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signature);
        return await CompleteAsync(id, signature.SignedByName, confirmWarnings, async (protocol, transaction) =>
        {
            if (signature.TenantSignature is not { Length: > 0 } || !IsPng(signature.TenantSignature))
            {
                throw new RuleViolationException("Bitte unterschreiben (Unterschrift des Mieters fehlt).");
            }

            var tenant = await SavePngAsync(signature.TenantSignature, "unterschrift-mieter.png", cancellationToken);
            var landlord = signature.LandlordSignature is { Length: > 0 } bytes && IsPng(bytes) ? await SavePngAsync(bytes, "unterschrift-vermieter.png", cancellationToken) : null;
            var request = await RequestAsync(protocol, tenant, landlord, cancellationToken, signature.SignedByName.Trim());
            var file = await StorePdfAsync(request, cancellationToken);
            return (file.Id, tenant.Id, landlord?.Id);
        }, cancellationToken);
    }

    public async Task<HandoverSignResult> SignWithScanAsync(Guid id, string signedByName, string fileName, Stream scan, bool confirmWarnings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return await CompleteAsync(id, signedByName, confirmWarnings, async (protocol, transaction) =>
        {
            var file = await SavePhotoAsync(scan, Path.GetFileName(fileName), cancellationToken);
            return (file.Id, (Guid?)null, (Guid?)null);
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        Command.ExecuteAsync(async () =>
        {
            var protocol = await db.Set<HandoverProtocol>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (protocol.Status != HandoverStatus.Draft)
            {
                throw new RuleViolationException("Ein unterschriebenes Protokoll kann nicht gelöscht werden.");
            }

            db.Remove(protocol);
            await db.SaveChangesAsync(cancellationToken);
            return id;
        });

    private static void Clean(HandoverContent content)
    {
        content.Keys = [.. content.Keys.Where(k => !string.IsNullOrWhiteSpace(k.Kind) || k.Count > 0)];
        content.Defects = [.. content.Defects.Where(d => !string.IsNullOrWhiteSpace(d.Description) || d.PhotoFileIds.Count > 0)];
        foreach (var defect in content.Defects)
        {
            defect.Room = defect.Room.Trim();
            defect.Description = defect.Description.Trim();
        }

        content.Remarks = string.IsNullOrWhiteSpace(content.Remarks) ? null : content.Remarks.Trim();
        content.NewStreet = string.IsNullOrWhiteSpace(content.NewStreet) ? null : content.NewStreet.Trim();
        content.NewPostalCode = string.IsNullOrWhiteSpace(content.NewPostalCode) ? null : content.NewPostalCode.Trim();
        content.NewCity = string.IsNullOrWhiteSpace(content.NewCity) ? null : content.NewCity.Trim();
        var withAddress = new[] { content.NewStreet, content.NewPostalCode, content.NewCity };
        if (withAddress.Any(a => a is not null) && withAddress.Any(a => a is null))
        {
            throw new RuleViolationException("Bitte die neue Anschrift vollständig angeben (Straße, PLZ, Ort) oder ganz leer lassen.");
        }
    }

    private static bool IsPng(byte[] bytes) => bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

    /// <summary>Common end of both ways to sign: files, meter readings, new address, status — all or nothing.</summary>
    private async Task<HandoverSignResult> CompleteAsync(
        Guid id,
        string signedByName,
        bool confirmWarnings,
        Func<HandoverProtocol, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction, Task<(Guid FileId, Guid? Tenant, Guid? Landlord)>> produce,
        CancellationToken cancellationToken)
    {
        try
        {
            var protocol = await db.Set<HandoverProtocol>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException();
            if (protocol.Status != HandoverStatus.Draft)
            {
                throw new RuleViolationException("Das Protokoll ist bereits unterschrieben.");
            }

            if (string.IsNullOrWhiteSpace(signedByName))
            {
                throw new RuleViolationException("Bitte den Namen der unterschreibenden Person angeben.");
            }

            var context = await LoadAsync(protocol.LeaseId, cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Readings first: warnings must be confirmed before anything is filed.
            var warnings = new List<string>();
            foreach (var reading in protocol.Content.Meters.Where(m => m.Value is not null))
            {
                var result = await meters.RecordReadingAsync(
                    reading.MeterId,
                    new ReadingInput
                    {
                        Date = protocol.Date,
                        Value = reading.Value,
                        Kind = ReadingType.InterimReading,
                        Note = $"Übergabeprotokoll {(protocol.Type == HandoverType.MoveIn ? "Einzug" : "Auszug")}",
                        Confirmed = confirmWarnings,
                    },
                    reading.PhotoFileId,
                    cancellationToken);
                if (result.NeedsConfirmation)
                {
                    warnings.AddRange(result.Warnings.Select(w => $"{reading.Name}: {w}"));
                }
                else if (!result.Succeeded)
                {
                    return new HandoverSignResult(false, [.. result.Errors.Select(e => $"{reading.Name}: {e}")], []);
                }
            }

            if (warnings.Count > 0)
            {
                return new HandoverSignResult(false, [], warnings);
            }

            var (fileId, tenantSignature, landlordSignature) = await produce(protocol, transaction);
            var title = $"Übergabeprotokoll {(protocol.Type == HandoverType.MoveIn ? "Einzug" : "Auszug")} – {context.TenantNames}";
            var document = await documents.CreateAsync(fileId, new DocumentInput
            {
                Title = title.Length > 200 ? title[..200] : title,
                Category = DocumentCategory.Minutes,
                Date = protocol.Date,
                LeaseId = protocol.LeaseId,
                SharedWithTenants = true,
                Note = "Unterschriebenes Übergabeprotokoll.",
            }, cancellationToken);
            if (!document.Succeeded)
            {
                throw new RuleViolationException(string.Join(' ', document.Errors));
            }

            if (protocol.Type == HandoverType.MoveOut && protocol.Content.NewStreet is { } street)
            {
                var from = (context.Lease.End ?? protocol.Date).AddDays(1);
                foreach (var person in context.Tenants)
                {
                    person.SetAddress(from, street, protocol.Content.NewPostalCode!, protocol.Content.NewCity!);
                }
            }

            protocol.Sign(signedByName, clock.GetUtcNow(), tenantSignature, landlordSignature, fileId, document.Id!.Value);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new HandoverSignResult(true, [], [], document.Id);
        }
        catch (NotFoundException)
        {
            return new HandoverSignResult(false, ["Das Protokoll wurde nicht gefunden."], []);
        }
        catch (RuleViolationException ex)
        {
            db.ChangeTracker.Clear();
            return new HandoverSignResult(false, [ex.Message], []);
        }
        catch (HttpRequestException)
        {
            db.ChangeTracker.Clear();
            return new HandoverSignResult(false, ["Der PDF-Dienst ist nicht erreichbar; es wurde nichts verändert."], []);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. no landlord profile yet (needed for the letterhead of the PDF)
            db.ChangeTracker.Clear();
            return new HandoverSignResult(false, [ex.Message], []);
        }
        catch (FileRejectedException ex)
        {
            db.ChangeTracker.Clear();
            return new HandoverSignResult(false, [ex.Message], []);
        }
    }

    private async Task<Domain.Files.StoredFile> SavePhotoAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            return await files.SaveAsync(content, fileName, cancellationToken);
        }
        catch (FileRejectedException ex)
        {
            throw new RuleViolationException(ex.Message);
        }
    }

    private async Task<Domain.Files.StoredFile> SavePngAsync(byte[] bytes, string name, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes);
        return await SavePhotoAsync(stream, name, cancellationToken);
    }

    private async Task<Domain.Files.StoredFile> StorePdfAsync(PdfRequest request, CancellationToken cancellationToken)
    {
        var name = string.Concat(request.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        return await pdf.GenerateAndStoreAsync(request, name + ".pdf", cancellationToken);
    }

    private async Task<PdfRequest> RequestAsync(HandoverProtocol protocol, Domain.Files.StoredFile? tenantSignature, Domain.Files.StoredFile? landlordSignature, CancellationToken cancellationToken, string? signedBy = null)
    {
        var context = await LoadAsync(protocol.LeaseId, cancellationToken);
        var content = protocol.Content;
        var isMoveOut = protocol.Type == HandoverType.MoveOut;
        IReadOnlyList<HandoverDifference> differences = [];
        var knownIds = new HashSet<Guid>();
        if (isMoveOut && await db.Set<HandoverProtocol>().AsNoTracking().SingleOrDefaultAsync(p => p.LeaseId == protocol.LeaseId && p.Type == HandoverType.MoveIn, cancellationToken) is { } moveIn)
        {
            differences = HandoverComparison.Compare(moveIn.Content, content);
            knownIds = [.. moveIn.Content.Defects.Select(d => d.Id)];
        }

        var defects = new List<object>();
        foreach (var defect in content.Defects)
        {
            var photos = new List<RawHtml>();
            foreach (var photoId in defect.PhotoFileIds)
            {
                photos.Add(await ImageAsync(photoId, 60, cancellationToken));
            }

            defects.Add(new { defect.Room, defect.Description, Known = defect.SourceId is { } s && knownIds.Contains(s), Photos = photos });
        }

        var readings = new List<object>();
        foreach (var meter in content.Meters)
        {
            readings.Add(new
            {
                meter.Name,
                meter.Number,
                meter.Value,
                Unit = meter.Unit,
                Photo = meter.PhotoFileId is { } photo ? await ImageAsync(photo, 35, cancellationToken) : null,
            });
        }

        var title = $"Übergabeprotokoll {(isMoveOut ? "Auszug" : "Einzug")}";
        var model = new
        {
            TypeName = title,
            HandoverDate = protocol.Date,
            RentalUnit = $"{context.RentalUnit.Name}, {context.RentalUnit.Property}",
            Tenants = context.TenantNames,
            LeaseStart = context.Lease.Start,
            LeaseEnd = context.Lease.End,
            Rooms = content.Rooms.Select(r => new { r.Name, Items = r.Items.Select(i => new { i.Component, Condition = ConditionText(i.Condition), i.Note }).ToList() }).ToList(),
            Defects = defects,
            Keys = content.Keys.Select(k => new { k.Kind, k.Count }).ToList(),
            Meters = readings,
            content.Remarks,
            IsMoveOut = isMoveOut,
            Differences = differences.Select(d => new { d.Room, d.Text, Kind = KindText(d.Kind) }).ToList(),
            NewAddress = content.NewStreet is null ? null : $"{content.NewStreet}, {content.NewPostalCode} {content.NewCity}",
            TenantSignature = tenantSignature is null ? null : await ImageAsync(tenantSignature.Id, 18, cancellationToken),
            LandlordSignature = landlordSignature is null ? null : await ImageAsync(landlordSignature.Id, 18, cancellationToken),
            SignedBy = signedBy ?? protocol.SignedByName ?? context.TenantNames,
            Landlord = context.LandlordName,
            SignedAt = protocol.SignedAt is { } at ? (DateOnly?)GermanTime.Today(at) : null,
        };
        return new PdfRequest("handover", title, model, null, null, GermanTime.Today(clock.GetUtcNow()), context.Unit.PropertyId);
    }

    private async Task<RawHtml> ImageAsync(Guid fileId, int heightMm, CancellationToken cancellationToken)
    {
        await using var opened = await files.OpenAsync(fileId, cancellationToken);
        if (opened is null)
        {
            return new RawHtml("");
        }

        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer, cancellationToken);
        return new RawHtml($"<img src=\"data:{opened.StoredFile.MimeType};base64,{Convert.ToBase64String(buffer.ToArray())}\" style=\"max-height: {heightMm}mm; max-width: 80mm; margin: 1mm\" alt=\"\">");
    }

    private static string ConditionText(ItemCondition condition) => condition switch
    {
        ItemCondition.Wear => "Gebrauchsspuren",
        ItemCondition.Defect => "Mangel",
        _ => "in Ordnung",
    };

    private static string KindText(DifferenceKind kind) => kind switch
    {
        DifferenceKind.Worse => "verschlechtert",
        DifferenceKind.NewDefect => "neu",
        DifferenceKind.KnownDefect => "schon beim Einzug",
        _ => "fehlt",
    };

    private static HandoverContent TakeOver(HandoverContent moveIn)
    {
        var copy = moveIn.Copy();
        foreach (var defect in copy.Defects)
        {
            defect.SourceId = defect.Id;
            defect.Id = Guid.NewGuid();
        }

        copy.Meters = [];
        copy.Remarks = null;
        return copy;
    }

    private static HandoverContent PrefillRooms(Context context)
    {
        var unit = context.Unit;
        List<string> names;
        if (context.Room is { } room)
        {
            names = [room.Name, "Küche (Gemeinschaft)", "Bad (Gemeinschaft)", "Flur (Gemeinschaft)"];
        }
        else
        {
            names = ["Flur", unit.FittedKitchen ? "Küche (mit Einbauküche)" : "Küche", string.IsNullOrWhiteSpace(unit.Bathroom) ? "Bad" : $"Bad ({unit.Bathroom})"];
            var rooms = Math.Max(1, (int)Math.Ceiling(unit.RoomCount ?? 1m));
            names.AddRange(Enumerable.Range(1, rooms).Select(n => $"Zimmer {n}"));
            if (unit.Balcony)
            {
                names.Add("Balkon / Terrasse");
            }
        }

        return new HandoverContent
        {
            Rooms = [.. names.Select(n => new HandoverRoom
            {
                Name = n,
                Items = [.. (n.StartsWith("Bad", StringComparison.Ordinal) ? BathComponents : StandardComponents).Select(c => new HandoverItem { Component = c })],
            })],
        };
    }

    private async Task<List<HandoverMeterReading>> MetersAsync(Context context, CancellationToken cancellationToken)
    {
        var rows = await meters.RoundAsync(context.RentalUnit.PropertyId, context.Unit.Id, cancellationToken);
        return [.. rows.Select(m => new HandoverMeterReading { MeterId = m.Id, Name = m.Name, Number = m.Number, Unit = Unit(m.MeasurementUnit) })];
    }

    private static string Unit(MeasurementUnit unit) => unit switch
    {
        MeasurementUnit.KilowattHours => "kWh",
        MeasurementUnit.MegawattHours => "MWh",
        MeasurementUnit.Liters => "l",
        _ => "m³",
    };

    private async Task<Context> LoadAsync(Guid leaseId, CancellationToken cancellationToken)
    {
        var lease = await db.Set<Lease>().AsNoTracking().Include(v => v.Parties).SingleOrDefaultAsync(v => v.Id == leaseId, cancellationToken) ?? throw new NotFoundException();
        var rentalUnit = (await RentalUnit.GetAsync(db, cancellationToken)).GetValueOrDefault(lease.RentalUnitId) ?? throw new NotFoundException();
        var unit = await db.Set<Unit>().AsNoTracking().Include(e => e.Room).SingleAsync(e => e.Id == lease.UnitId, cancellationToken);
        var room = lease.RoomId is { } roomId ? unit.Room.SingleOrDefault(z => z.Id == roomId) : null;
        var ids = lease.Tenant.Select(p => p.PersonId).ToList();
        var people = await db.Set<Person>().Include(p => p.Addresses).Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var tenants = ids.Select(i => people.First(p => p.Id == i)).ToList();
        var landlord = await SenderProfiles.ForPropertyAsync(db, unit.PropertyId, cancellationToken);
        return new Context(lease, rentalUnit, unit, room, tenants, string.Join(" und ", tenants.Select(p => p.DisplayName)), landlord?.Name ?? "Vermieter");
    }

    private sealed record Context(Lease Lease, RentalUnit RentalUnit, Unit Unit, Room? Room, List<Person> Tenants, string TenantNames, string LandlordName);
}
