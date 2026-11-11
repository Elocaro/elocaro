using Elocaro.Application.Common;

namespace Elocaro.Application.Operations;

public enum GoogleClientSource
{
    None,

    /// <summary>Entered in the app (stored in the database).</summary>
    App,

    /// <summary><c>GoogleDrive__ClientId</c> and <c>GoogleDrive__ClientSecret</c> in the environment.</summary>
    Environment,

    /// <summary>Built into the published image when it was built (release pipeline); can be overridden by the other two.</summary>
    BuiltIn,
}

/// <summary>The OAuth client in use: where it comes from and its ID. The secret is never part of it.</summary>
public sealed record GoogleClientView(GoogleClientSource Source, string? ClientId);

/// <summary>The OAuth client of this installation for the sign-in at Google (Google Drive backup), maintained in the app.</summary>
public interface IGoogleClientSettings
{
    Task<GoogleClientView> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the client; an empty <paramref name="clientSecret"/> keeps the saved one. Takes precedence over the environment.</summary>
    Task<Result> SaveAsync(string clientId, string? clientSecret, CancellationToken cancellationToken = default);

    /// <summary>Removes the client entered in the app (a client from the environment applies again, if there is one).</summary>
    Task<Result> RemoveAsync(CancellationToken cancellationToken = default);
}
