using MietHahn.Application.Documents;
using MietHahn.Application.Files;
using MietHahn.Domain.Files;

namespace MietHahn.Web.Tests;

/// <summary>Stand-in for Gotenberg: stores a minimal PDF.</summary>
internal sealed class FakePdfGenerator(IFileStore fileStore) : IPdfGenerator
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    public Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default) => Task.FromResult(Pdf);

    public async Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new MemoryStream(Pdf);
        return await fileStore.SaveAsync(content, fileName, cancellationToken);
    }
}
