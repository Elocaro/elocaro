using UglyToad.PdfPig;

namespace Elocaro.Infrastructure.Receipts.Extraction;

internal static class EInvoiceProbe
{
    public static int Count(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.Advanced.TryGetEmbeddedFiles(out var files) ? files.Count : 0;
    }
}
