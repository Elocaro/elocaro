using Elocaro.Application.Leases;
using Elocaro.Domain.Leases;

namespace Elocaro.Web.Components.Pages.Admin.Leases;

/// <summary>Texts of the handover protocol page.</summary>
public static class HandoverTexts
{
    public static string Condition(ItemCondition value) => value switch
    {
        ItemCondition.Wear => "Gebrauchsspuren",
        ItemCondition.Defect => "Mangel",
        _ => "in Ordnung",
    };

    public static string Difference(DifferenceKind kind) => kind switch
    {
        DifferenceKind.Worse => "verschlechtert",
        DifferenceKind.NewDefect => "neu",
        DifferenceKind.KnownDefect => "schon beim Einzug",
        _ => "fehlt",
    };

    /// <summary>Short remark behind the room name: how many defects or signs of wear were noted.</summary>
    public static string RoomSummary(HandoverRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var defects = room.Items.Count(i => i.Condition == ItemCondition.Defect);
        var wear = room.Items.Count(i => i.Condition == ItemCondition.Wear);
        return defects > 0 ? $"· {defects} Mangel" : wear > 0 ? $"· {wear}× Gebrauchsspuren" : "";
    }
}
