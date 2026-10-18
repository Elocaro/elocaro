namespace MietHahn.Infrastructure.Pdf;

/// <summary>Konfigurationsabschnitt <c>Pdf</c>.</summary>
public sealed class PdfOptionen
{
    public const string Abschnitt = "Pdf";

    /// <summary>Basis-URL des Gotenberg-Dienstes (im Docker-Netz, nicht öffentlich).</summary>
    public Uri GotenbergUrl { get; set; } = new("http://gotenberg:3000");

    /// <summary>PDF/A-Variante für die Langzeitarchivierung; leer = kein PDF/A.</summary>
    public string? PdfA { get; set; } = "PDF/A-2b";

    public int TimeoutSekunden { get; set; } = 60;
}
