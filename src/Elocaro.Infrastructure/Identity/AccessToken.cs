using System.Security.Cryptography;
using System.Text;
using Elocaro.Domain.Common;

namespace Elocaro.Infrastructure.Identity;

/// <summary>
/// Personal access token for integrations such as the MCP interface (see <c>IAccessTokens</c>). Only the SHA-256 hash is stored; the secret appears once, when it is created.
/// Creating and revoking are audited (without the hash), using the token is not an entity change.
/// </summary>
[NoExport]
public sealed class AccessToken : Entity
{
    public const string Prefix = "elo_";

    private AccessToken()
    {
        Name = "";
        TokenHash = "";
        TokenStart = "";
        Scopes = [];
    }

    private AccessToken(Guid userId, string name, string tokenHash, string tokenStart, string[] scopes, DateTimeOffset expiresAt)
    {
        UserId = userId;
        Name = name;
        TokenHash = tokenHash;
        TokenStart = tokenStart;
        Scopes = scopes;
        ExpiresAt = expiresAt;
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; }

    [Confidential]
    public string TokenHash { get; private set; }

    /// <summary>First characters of the secret, to recognise the token in the list.</summary>
    public string TokenStart { get; private set; }

    /// <summary>Scope names, see <c>IAccessTokens.Scopes</c>.</summary>
    public string[] Scopes { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Creates a token and returns the plaintext secret (for showing once).</summary>
    public static (AccessToken Token, string Secret) Generate(Guid userId, string name, IEnumerable<string> scopes, DateTimeOffset expiresAt)
    {
        var secret = Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new AccessToken(userId, name, Hash(secret), secret[..(Prefix.Length + 4)], scopes.Distinct().Order().ToArray(), expiresAt), secret);
    }

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;
}
