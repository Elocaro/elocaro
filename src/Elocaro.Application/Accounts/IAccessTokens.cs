using System.Security.Claims;

namespace Elocaro.Application.Accounts;

/// <summary>
/// Personal access tokens: a way for integrations (currently the MCP interface for AI assistants) to act for one account,
/// without the sign-in. A token belongs to one account and never grants more than that account may do: the rights are
/// recomputed on every request (see <see cref="AuthenticateAsync"/>). What a token may be used for is decided by the endpoint
/// that accepts it (policy and scopes), so a new integration only needs a policy, not a new kind of token.
/// </summary>
public interface IAccessTokens
{
    /// <summary>Claim on the principal of a token request: the id of the token.</summary>
    public const string TokenIdClaim = "elocaro:access-token";

    /// <summary>Claim on the principal of a token request: one per write scope of the token (only while <c>Mcp:AllowWrite</c> is on).</summary>
    public const string WriteScopeClaim = "elocaro:write-scope";

    /// <summary>The write scope for master data (create and change properties, people, leases, meters).</summary>
    public const string WriteMasterData = "write:masterdata";

    /// <summary>Roles that may hold a token.</summary>
    public static bool MayHoldToken(IEnumerable<string> roles) => roles.Any(r => r is Roles.Admin or Roles.Manager);

    /// <summary>The rights a token can carry at all, with their scope names.</summary>
    public static IReadOnlyList<AccessScope> Scopes { get; } =
    [
        new("read:masterdata", AccessRight.MasterData, "Stammdaten lesen (Objekte, Mieter, Verträge, Zähler, Dokumente)"),
        new("read:finance", AccessRight.Finance, "Finanzen lesen (Mieterkonten, Belege, Bank, Abrechnungen)"),
        new(WriteMasterData, AccessRight.MasterData, "Stammdaten schreiben: Objekte, Einheiten, Personen, Mietverträge und Zähler anlegen und ändern (schließt Lesen ein; jede Aktion steht in der Aktivität und lässt sich dort rückgängig machen)", Write: true),
    ];

    /// <summary>Whether tokens can be used at all: at least one integration is switched on, and this is no demo instance.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether write scopes can be chosen and take effect (<c>Mcp:AllowWrite</c>).</summary>
    bool WriteEnabled { get; }

    /// <summary>Longest validity of a new token in days.</summary>
    int MaxDays { get; }

    /// <summary>Tokens of the signed-in account (never the secret itself).</summary>
    Task<IReadOnlyList<AccessTokenInfo>> ListAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a token for the signed-in account. Only scopes the account itself has can be chosen. The secret is part of the
    /// result and appears only once.
    /// </summary>
    Task<AccessTokenResult> CreateAsync(ClaimsPrincipal actor, string name, IReadOnlyCollection<string> scopes, int validDays, CancellationToken cancellationToken = default);

    /// <summary>Revokes a token of the signed-in account.</summary>
    Task<bool> RevokeAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a token and builds the principal for the request: current account state (blocked, role, two-factor) and current
    /// rights, cut down to the scopes of the token. <c>null</c> for anything that does not pass.
    /// </summary>
    Task<ClaimsPrincipal?> AuthenticateAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes one audit entry for a use of the token: which token, what was done (e.g. "MCP list_properties") and a hash of
    /// the arguments (never the content).
    /// </summary>
    Task RecordUseAsync(ClaimsPrincipal user, string action, string argumentsHash, CancellationToken cancellationToken = default);
}

public sealed record AccessScope(string Name, AccessRight Right, string Description, bool Write = false);

public sealed record AccessTokenInfo(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt)
{
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}

public sealed record AccessTokenResult(bool Succeeded, string? Error, string? Secret, Guid? Id)
{
    public static AccessTokenResult Failed(string error) => new(false, error, null, null);
}
