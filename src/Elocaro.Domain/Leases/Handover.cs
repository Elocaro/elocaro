using Elocaro.Domain.Common;

namespace Elocaro.Domain.Leases;

public enum HandoverType
{
    MoveIn = 0,
    MoveOut = 1,
}

public enum HandoverStatus
{
    /// <summary>Being filled in; can still be changed.</summary>
    Draft = 0,

    /// <summary>Signed (on the device or as a signed printout); the content no longer changes.</summary>
    Completed = 1,
}

public enum ItemCondition
{
    /// <summary>Without objection.</summary>
    Good = 0,

    /// <summary>Signs of normal use.</summary>
    Wear = 1,

    /// <summary>Damage or defect.</summary>
    Defect = 2,
}

/// <summary>Part of a room (walls, floor, windows …) with its condition.</summary>
public sealed class HandoverItem
{
    public string Component { get; set; } = "";

    public ItemCondition Condition { get; set; }

    public string? Note { get; set; }
}

public sealed class HandoverRoom
{
    public string Name { get; set; } = "";

    public List<HandoverItem> Items { get; set; } = [];
}

/// <summary>A defect with photos. <see cref="SourceId"/> is set when it was taken over from the move-in protocol.</summary>
public sealed class HandoverDefect
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SourceId { get; set; }

    public string Room { get; set; } = "";

    public string Description { get; set; } = "";

    public List<Guid> PhotoFileIds { get; set; } = [];
}

public sealed class HandoverKey
{
    public string Kind { get; set; } = "";

    public int Count { get; set; }
}

/// <summary>Meter reading at the handover; becomes an interim reading of the meter.</summary>
public sealed class HandoverMeterReading
{
    public Guid MeterId { get; set; }

    public string Name { get; set; } = "";

    public string Number { get; set; } = "";

    public string Unit { get; set; } = "";

    public decimal? Value { get; set; }

    public Guid? PhotoFileId { get; set; }
}

/// <summary>The filled-in part of a protocol (stored as JSON).</summary>
public sealed class HandoverContent
{
    public List<HandoverRoom> Rooms { get; set; } = [];

    public List<HandoverDefect> Defects { get; set; } = [];

    public List<HandoverKey> Keys { get; set; } = [];

    public List<HandoverMeterReading> Meters { get; set; } = [];

    public string? Remarks { get; set; }

    /// <summary>Move-out: new address of the tenants (for the utility statement and the deposit settlement).</summary>
    public string? NewStreet { get; set; }

    public string? NewPostalCode { get; set; }

    public string? NewCity { get; set; }

    public HandoverContent Copy() => System.Text.Json.JsonSerializer.Deserialize<HandoverContent>(System.Text.Json.JsonSerializer.Serialize(this))!;
}

/// <summary>Handover protocol of a lease at move-in or move-out; one per lease and type.</summary>
public sealed class HandoverProtocol : Entity
{
    private HandoverProtocol()
    {
        Content = new HandoverContent();
    }

    public HandoverProtocol(Guid leaseId, HandoverType type, DateOnly date, HandoverContent content)
    {
        LeaseId = leaseId;
        Type = type;
        Date = date;
        Content = content;
    }

    public Guid LeaseId { get; private set; }

    public HandoverType Type { get; private set; }

    public DateOnly Date { get; private set; }

    public HandoverStatus Status { get; private set; }

    public HandoverContent Content { get; private set; }

    /// <summary>Name of the person who signed for the tenants.</summary>
    public string? SignedByName { get; private set; }

    public DateTimeOffset? SignedAt { get; private set; }

    public Guid? TenantSignatureFileId { get; private set; }

    public Guid? LandlordSignatureFileId { get; private set; }

    /// <summary>PDF of the signed protocol, or the scan of the signed printout.</summary>
    public Guid? DocumentFileId { get; private set; }

    public Guid? DocumentId { get; private set; }

    public void Edit(DateOnly date, HandoverContent content)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(content);
        if (content.Keys.Any(k => k.Count < 0 || string.IsNullOrWhiteSpace(k.Kind)) || content.Meters.Any(m => m.Value is < 0))
        {
            throw new RuleViolationException("Schlüssel brauchen eine Bezeichnung und eine Anzahl, Zählerstände dürfen nicht negativ sein.");
        }

        Date = date;
        Content = content;
    }

    public void Sign(string signedByName, DateTimeOffset timestamp, Guid? tenantSignature, Guid? landlordSignature, Guid documentFileId, Guid documentId)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(signedByName))
        {
            throw new RuleViolationException("Bitte den Namen der unterschreibenden Person angeben.");
        }

        SignedByName = signedByName.Trim();
        SignedAt = timestamp;
        TenantSignatureFileId = tenantSignature;
        LandlordSignatureFileId = landlordSignature;
        DocumentFileId = documentFileId;
        DocumentId = documentId;
        Status = HandoverStatus.Completed;
    }

    private void EnsureDraft()
    {
        if (Status != HandoverStatus.Draft)
        {
            throw new RuleViolationException("Das Protokoll ist unterschrieben und kann nicht mehr geändert werden.");
        }
    }
}

public enum DifferenceKind
{
    /// <summary>Condition got worse than at move-in.</summary>
    Worse,

    /// <summary>Defect that was not noted at move-in.</summary>
    NewDefect,

    /// <summary>Defect already noted at move-in.</summary>
    KnownDefect,

    /// <summary>Fewer keys handed back than received.</summary>
    MissingKeys,
}

public sealed record HandoverDifference(DifferenceKind Kind, string Room, string Text);

/// <summary>Comparison of the move-out with the move-in protocol: which defects are new.</summary>
public static class HandoverComparison
{
    public static IReadOnlyList<HandoverDifference> Compare(HandoverContent moveIn, HandoverContent moveOut)
    {
        ArgumentNullException.ThrowIfNull(moveIn);
        ArgumentNullException.ThrowIfNull(moveOut);
        var differences = new List<HandoverDifference>();

        foreach (var room in moveOut.Rooms)
        {
            var before = moveIn.Rooms.FirstOrDefault(r => string.Equals(r.Name, room.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var item in room.Items)
            {
                var was = before?.Items.FirstOrDefault(i => string.Equals(i.Component, item.Component, StringComparison.OrdinalIgnoreCase));
                if (item.Condition > (was?.Condition ?? ItemCondition.Good))
                {
                    differences.Add(new HandoverDifference(DifferenceKind.Worse, room.Name,
                        $"{item.Component}: {Name(was?.Condition ?? ItemCondition.Good)} → {Name(item.Condition)}{(string.IsNullOrWhiteSpace(item.Note) ? "" : " (" + item.Note + ")")}"));
                }
            }
        }

        foreach (var defect in moveOut.Defects)
        {
            var known = defect.SourceId is { } source && moveIn.Defects.Any(d => d.Id == source);
            differences.Add(new HandoverDifference(known ? DifferenceKind.KnownDefect : DifferenceKind.NewDefect, defect.Room, defect.Description));
        }

        foreach (var key in moveIn.Keys)
        {
            var returned = moveOut.Keys.Where(k => string.Equals(k.Kind, key.Kind, StringComparison.OrdinalIgnoreCase)).Sum(k => k.Count);
            if (returned < key.Count)
            {
                differences.Add(new HandoverDifference(DifferenceKind.MissingKeys, "Schlüssel", $"{key.Kind}: {key.Count - returned} von {key.Count} fehlen"));
            }
        }

        return differences;
    }

    private static string Name(ItemCondition condition) => condition switch
    {
        ItemCondition.Wear => "Gebrauchsspuren",
        ItemCondition.Defect => "Mangel",
        _ => "in Ordnung",
    };
}
