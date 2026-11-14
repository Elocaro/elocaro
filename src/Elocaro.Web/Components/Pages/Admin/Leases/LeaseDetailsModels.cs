using System.ComponentModel.DataAnnotations;
using Elocaro.Domain.Leases;

namespace Elocaro.Web.Components.Pages.Admin.Leases;

/// <summary>Form of the dialog "end rent component" on the lease page.</summary>
public sealed class EndModel
{
    public RentComponentType Kind { get; set; }

    [Required(ErrorMessage = "Bitte den letzten Tag angeben.")]
    public DateOnly? LastDay { get; set; }
}

/// <summary>Form of the dialog "add tenant or guarantor" on the lease page.</summary>
public sealed class PartyModel
{
    [Required(ErrorMessage = "Bitte eine Person wählen.")]
    public Guid? PersonId { get; set; }

    public PartyRole Role { get; set; }
}

/// <summary>Selection of the room to add to the lease.</summary>
public sealed class RoomModel
{
    public Guid? RoomId { get; set; }
}
