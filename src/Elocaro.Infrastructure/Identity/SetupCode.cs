using System.Security.Cryptography;
using System.Text;
using Elocaro.Application.Accounts;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Identity;

/// <summary>Configuration section <c>Setup</c>.</summary>
public sealed class SetupOptions
{
    public const string Section = "Setup";

    /// <summary>Code for the initial setup (<c>Setup__Code</c>). Empty: a random code per process start.</summary>
    public string? Code { get; set; }
}

internal sealed class SetupCode : ISetupCode
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
    private const int GeneratedLength = 32;

    public SetupCode(IOptions<SetupOptions> options)
    {
        var configured = options.Value.Code?.Trim();
        IsGenerated = string.IsNullOrEmpty(configured);
        Value = IsGenerated ? RandomNumberGenerator.GetString(Alphabet, GeneratedLength) : configured!;
    }

    public string Value { get; }

    public bool IsGenerated { get; }

    public bool Matches(string? candidate) =>
        // Hashing makes both sides the same length, so the comparison also does not reveal the length of the code.
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(candidate?.Trim() ?? "")),
            SHA256.HashData(Encoding.UTF8.GetBytes(Value)));
}
