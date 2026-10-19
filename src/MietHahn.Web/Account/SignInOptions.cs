namespace MietHahn.Web.Account;

/// <summary>Configuration section <c>Anmeldung</c>.</summary>
public sealed class SignInOptions
{
    public const string Section = "SignIn";

    /// <summary>Allowed POST requests to <c>/konto/*</c> per IP address and minute (protection against brute force).</summary>
    public int RequestsPerMinute { get; set; } = 10;

    /// <summary>How long a session with "remember me" or via sign-in link stays valid.</summary>
    public int SessionDays { get; set; } = 14;
}
