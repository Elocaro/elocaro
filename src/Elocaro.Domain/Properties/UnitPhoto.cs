using Elocaro.Domain.Common;

namespace Elocaro.Domain.Properties;

/// <summary>A photo of a rental unit (a unit, or one room of a shared flat) for the exposé. The photo itself lives in the file store.</summary>
public sealed class UnitPhoto : Entity
{
    public const int MaxCaption = 200;

    /// <summary>At most this many photos per unit or room (an exposé has to stay a few pages).</summary>
    public const int MaxPerUnit = 12;

    private UnitPhoto()
    {
    }

    public UnitPhoto(Guid rentalUnitId, Guid fileId, string? caption, int order)
    {
        RentalUnitId = rentalUnitId;
        FileId = fileId;
        Order = order;
        SetCaption(caption);
    }

    public void SetCaption(string? caption)
    {
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (Caption is { Length: > MaxCaption })
        {
            throw new RuleViolationException("Die Bildunterschrift darf höchstens 200 Zeichen lang sein.");
        }
    }

    public void MoveTo(int order) => Order = order;

    /// <summary>The id of the unit or of the room (what can be rented).</summary>
    public Guid RentalUnitId { get; private set; }

    public Guid FileId { get; private set; }

    public string? Caption { get; private set; }

    public int Order { get; private set; }
}
