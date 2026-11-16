using Elocaro.Application.Common;

namespace Elocaro.Application.Properties;

/// <summary>Photos of a rental unit or a room of a shared flat (for the exposé).</summary>
public interface IUnitPhotos
{
    /// <summary>The photos of the unit or room in their order.</summary>
    Task<IReadOnlyList<UnitPhotoRow>> ListAsync(Guid rentalUnitId, CancellationToken cancellationToken = default);

    /// <summary>Adds a photo (a file of the file store that is an image) at the end. Fails above 12 photos or for an unknown unit or room.</summary>
    Task<Result> AddAsync(Guid rentalUnitId, Guid fileId, string? caption, CancellationToken cancellationToken = default);

    Task<Result> SetCaptionAsync(Guid id, string? caption, CancellationToken cancellationToken = default);

    /// <summary>Moves the photo one place to the front (<paramref name="up"/>) or to the back.</summary>
    Task<Result> MoveAsync(Guid id, bool up, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed record UnitPhotoRow(Guid Id, Guid FileId, string? Caption, int Order);
