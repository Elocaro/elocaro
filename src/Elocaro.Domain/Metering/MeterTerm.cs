using Elocaro.Domain.Common;

namespace Elocaro.Domain.Metering;

/// <summary>A source meter of a calculated meter with its sign (+1 added, −1 subtracted).</summary>
public sealed class MeterTerm : Entity
{
    private MeterTerm()
    {
    }

    internal MeterTerm(Guid meterId, Guid sourceMeterId, int sign)
    {
        MeterId = meterId;
        SourceMeterId = sourceMeterId;
        Sign = sign;
    }

    public Guid MeterId { get; private set; }

    public Guid SourceMeterId { get; private set; }

    public int Sign { get; private set; }
}
