namespace MietHahn.Infrastructure;

/// <summary>Configuration section <c>Application</c>.</summary>
public sealed class ApplicationOptions
{
    public const string Section = "Application";

    /// <summary>
    /// Public base URL (e.g. <c>https://miethahn.example.de/</c>) for links in emails.
    /// Deliberately configured rather than derived from the host header (protection against host header injection).
    /// </summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:5080/");

    public Uri Link(string relativePath) => new(BaseUrl, relativePath.TrimStart('/'));
}
