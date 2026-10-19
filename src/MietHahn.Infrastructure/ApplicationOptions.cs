namespace MietHahn.Infrastructure;

/// <summary>Konfigurationsabschnitt <c>Anwendung</c>.</summary>
public sealed class ApplicationOptions
{
    public const string Section = "Application";

    /// <summary>
    /// Öffentliche Basis-URL (z. B. <c>https://miethahn.example.de/</c>) für Links in E-Mails.
    /// Bewusst konfiguriert statt aus dem Host-Header abgeleitet (Schutz vor Host-Header-Injection).
    /// </summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:5080/");

    public Uri Link(string relativePath) => new(BaseUrl, relativePath.TrimStart('/'));
}
