using System.Security.Claims;
using Elocaro.Application.Accounts;
using Elocaro.Application.Admin;
using Elocaro.Application.Audit;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Identity;

/// <remarks>
/// The principal of a token request is not a copy of the sign-in: it is built new from the database on every request, as
/// "Verwalter" with exactly the rights the account has now AND the token carries. Admins therefore also appear as managers:
/// the existing checks in the services (<c>IsInRole(Admin) || HasClaim(permission)</c>) then see the cut-down rights.
/// Blocked accounts, role changes and withdrawn permissions take effect immediately.
/// </remarks>
internal sealed class AccessTokens(
    ElocaroDbContext db,
    UserManager<ApplicationUser> userManager,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    AuditWriter audit,
    IOptions<AccessTokenOptions> options,
    IOptions<McpOptions> mcp,
    IOptions<DemoOptions> demo,
    TimeProvider clock) : IAccessTokens
{
    private const int MaxActiveTokens = 20;

    private static readonly TimeSpan UsageResolution = TimeSpan.FromMinutes(1);

    // The MCP interface is the only integration so far; tokens are only of use while one is switched on. Add further ones here.
    public bool IsAvailable => !demo.Value.Enabled && mcp.Value.Enabled;

    public int MaxDays => options.Value.MaxDays;

    public async Task<IReadOnlyList<AccessTokenInfo>> ListAsync(ClaimsPrincipal actor, CancellationToken cancellationToken = default)
    {
        if (!TryUserId(actor, out var userId))
        {
            return [];
        }

        var tokens = await db.Set<AccessToken>().AsNoTracking().Where(t => t.UserId == userId).ToListAsync(cancellationToken);
        return tokens
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AccessTokenInfo(t.Id, t.Name, t.TokenStart, t.Scopes, t.CreatedAt, t.ExpiresAt, t.LastUsedAt, t.RevokedAt))
            .ToList();
    }

    public async Task<AccessTokenResult> CreateAsync(
        ClaimsPrincipal actor, string name, IReadOnlyCollection<string> scopes, int validDays, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (!IsAvailable)
        {
            return AccessTokenResult.Failed("Zugriffstoken sind auf diesem Server nicht eingeschaltet.");
        }

        if (!TryUserId(actor, out var userId) || await CurrentAccountAsync(userId) is not { } account)
        {
            return AccessTokenResult.Failed("Dafür fehlt die Berechtigung.");
        }

        var chosen = IAccessTokens.Scopes.Where(s => scopes.Contains(s.Name)).ToList();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return AccessTokenResult.Failed("Bitte einen Namen mit höchstens 100 Zeichen angeben.");
        }

        if (chosen.Count == 0 || chosen.Count != scopes.Distinct().Count() || chosen.Any(s => !account.Rights.Contains(s.Right)))
        {
            return AccessTokenResult.Failed("Es können nur Bereiche gewählt werden, für die das eigene Konto berechtigt ist.");
        }

        if (validDays < 1 || validDays > options.Value.MaxDays)
        {
            return AccessTokenResult.Failed($"Die Gültigkeit muss zwischen 1 und {options.Value.MaxDays} Tagen liegen.");
        }

        var now = clock.GetUtcNow();
        var active = await db.Set<AccessToken>().CountAsync(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now, cancellationToken);
        if (active >= MaxActiveTokens)
        {
            return AccessTokenResult.Failed($"Es sind höchstens {MaxActiveTokens} aktive Zugriffstoken möglich. Bitte nicht benötigte widerrufen.");
        }

        var (token, secret) = AccessToken.Generate(userId, name.Trim(), chosen.Select(s => s.Name), now.AddDays(validDays));
        db.Add(token);
        await db.SaveChangesAsync(cancellationToken);
        return new AccessTokenResult(true, null, secret, token.Id);
    }

    public async Task<bool> RevokeAsync(ClaimsPrincipal actor, Guid id, CancellationToken cancellationToken = default)
    {
        if (!TryUserId(actor, out var userId))
        {
            return false;
        }

        var token = await db.Set<AccessToken>().SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);
        if (token is null)
        {
            return false;
        }

        token.Revoke(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await audit.LogAsync(nameof(AccessToken), token.Id, AuditAction.Modified, [new FieldChange("Revoked", "false", "true")], cancellationToken);
        return true;
    }

    public async Task<ClaimsPrincipal?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(token) || !token.StartsWith(AccessToken.Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = AccessToken.Hash(token);
        var now = clock.GetUtcNow();
        var stored = await db.Set<AccessToken>().AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null || !stored.IsUsable(now))
        {
            return null;
        }

        // The DbContext may be older than this request (see UserManagement); the account is read fresh.
        if (await CurrentAccountAsync(stored.UserId) is not { } account)
        {
            return null;
        }

        var rights = EffectiveRights(account.Rights, stored.Scopes);
        if (rights.Count == 0)
        {
            return null;
        }

        if (stored.LastUsedAt is null || now - stored.LastUsedAt >= UsageResolution)
        {
            // No entity save: the use of a token is not a change worth an audit entry.
            await db.Set<AccessToken>().Where(t => t.Id == stored.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.LastUsedAt, now), cancellationToken);
        }

        return await BuildPrincipalAsync(account.User, stored.Id, rights);
    }

    public async Task RecordUseAsync(ClaimsPrincipal user, string action, string argumentsHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Guid.TryParse(user.FindFirstValue(IAccessTokens.TokenIdClaim), out var tokenId))
        {
            return;
        }

        db.Add(new AuditEntry(clock.GetUtcNow(), user.Identity?.Name ?? "unbekannt", nameof(AccessToken), tokenId, AuditAction.Read, "[]", $"{action} {argumentsHash}"));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ClaimsPrincipal> BuildPrincipalAsync(ApplicationUser user, Guid tokenId, IReadOnlyList<AccessRight> rights)
    {
        var full = await claimsFactory.CreateAsync(user);
        var identity = new ClaimsIdentity("ElocaroAccessToken", ClaimsIdentity.DefaultNameClaimType, ClaimsIdentity.DefaultRoleClaimType);
        var source = full.Identities.First();
        foreach (var claim in source.Claims.Where(c => c.Type != ClaimTypes.Role && c.Type != Permissions.ClaimType))
        {
            identity.AddClaim(claim);
        }

        identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Manager));
        foreach (var right in rights)
        {
            identity.AddClaim(new Claim(Permissions.ClaimType, right.ToString()));
        }

        identity.AddClaim(new Claim(IAccessTokens.TokenIdClaim, tokenId.ToString()));
        return new ClaimsPrincipal(identity);
    }

    /// <summary>The account as it is now; <c>null</c> if it may not hold a token (blocked, wrong role, missing two-factor).</summary>
    private async Task<Account?> CurrentAccountAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        await db.Entry(user).ReloadAsync();
        var roles = await userManager.GetRolesAsync(user);
        if (user.Locked || !IAccessTokens.MayHoldToken(roles))
        {
            return null;
        }

        // Admins and managers have to use two-factor to sign in; a token must not get around it.
        if (!user.TwoFactorEnabled)
        {
            return null;
        }

        var rights = roles.Contains(Roles.Admin)
            ? Permissions.All.ToList()
            : (await userManager.GetClaimsAsync(user)).Where(c => c.Type == Permissions.ClaimType).Select(c => Enum.Parse<AccessRight>(c.Value)).ToList();
        return new Account(user, rights);
    }

    /// <summary>Rights of the account that the scopes of the token cover. Nothing but the read scopes ever applies.</summary>
    private static List<AccessRight> EffectiveRights(IReadOnlyCollection<AccessRight> accountRights, IEnumerable<string> scopes) =>
        IAccessTokens.Scopes.Where(s => scopes.Contains(s.Name) && accountRights.Contains(s.Right)).Select(s => s.Right).ToList();

    private bool TryUserId(ClaimsPrincipal actor, out Guid userId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Guid.TryParse(userManager.GetUserId(actor), out userId);
    }

    private sealed record Account(ApplicationUser User, IReadOnlyList<AccessRight> Rights);
}
