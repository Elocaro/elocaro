namespace MietHahn.Infrastructure;

/// <summary>Konfigurationsabschnitt <c>Anwendung</c>.</summary>
public sealed class AnwendungOptionen
{
    public const string Abschnitt = "Anwendung";

    /// <summary>
    /// Öffentliche Basis-URL (z. B. <c>https://miethahn.example.de/</c>) für Links in E-Mails.
    /// Bewusst konfiguriert statt aus dem Host-Header abgeleitet (Schutz vor Host-Header-Injection).
    /// </summary>
    public Uri BasisUrl { get; set; } = new("http://localhost:5080/");

    public Uri Link(string relativ) => new(BasisUrl, relativ.TrimStart('/'));
}
