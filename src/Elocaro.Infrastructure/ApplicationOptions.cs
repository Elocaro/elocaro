namespace Elocaro.Infrastructure;

/// <summary>Configuration section <c>Application</c>.</summary>
public sealed class ApplicationOptions
{
    public const string Section = "Application";

    /// <summary>
    /// Public base URL (e.g. <c>https://elocaro.example.de/</c>) for links in emails.
    /// Deliberately configured rather than derived from the host header (protection against host header injection).
    /// </summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:5080/");

    /// <summary>
    /// A public address was configured (not the development default on localhost). Only then do printed documents carry a link
    /// or QR code, because it would not work for the recipient otherwise.
    /// </summary>
    public bool HasPublicBaseUrl => !BaseUrl.IsLoopback;

    public Uri Link(string relativePath) => new(BaseUrl, relativePath.TrimStart('/'));
}
