namespace MietHahn.Infrastructure.Pdf;

/// <summary>Configuration section <c>Pdf</c>.</summary>
public sealed class PdfOptions
{
    public const string Section = "Pdf";

    /// <summary>Base URL of the Gotenberg service (inside the Docker network, not public).</summary>
    public Uri GotenbergUrl { get; set; } = new("http://gotenberg:3000");

    /// <summary>PDF/A variant for long-term archiving; empty = no PDF/A.</summary>
    public string? PdfA { get; set; } = "PDF/A-2b";

    public int TimeoutSeconds { get; set; } = 60;
}
