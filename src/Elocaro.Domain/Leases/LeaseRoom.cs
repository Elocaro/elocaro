using Elocaro.Domain.Common;

namespace Elocaro.Domain.Leases;

/// <summary>A further room of a lease on several rooms of a shared flat.</summary>
public sealed class LeaseRoom : Entity
{
    private LeaseRoom()
    {
    }

    internal LeaseRoom(Guid leaseId, Guid roomId)
    {
        LeaseId = leaseId;
        RoomId = roomId;
    }

    public Guid LeaseId { get; private set; }

    public Guid RoomId { get; private set; }
}
