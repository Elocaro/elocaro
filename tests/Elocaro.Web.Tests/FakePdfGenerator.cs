using Elocaro.Application.Documents;
using Elocaro.Application.Files;
using Elocaro.Domain.Files;

namespace Elocaro.Web.Tests;

/// <summary>Stand-in for Gotenberg: stores a minimal PDF.</summary>
internal sealed class FakePdfGenerator(IFileStore fileStore) : IPdfGenerator
{
    private static readonly byte[] Pdf = "%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n"u8.ToArray();

    public Task<byte[]> GenerateAsync(PdfRequest job, CancellationToken cancellationToken = default) => Task.FromResult(Pdf);

    public Task<string> RenderHtmlAsync(PdfRequest job, CancellationToken cancellationToken = default) =>
        Task.FromResult("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body><h1>" + System.Net.WebUtility.HtmlEncode(job.Title) + "</h1>" + (job.TemplateText ?? "") + "</body></html>");

    public async Task<StoredFile> GenerateAndStoreAsync(PdfRequest job, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new MemoryStream(Pdf);
        return await fileStore.SaveAsync(content, fileName, cancellationToken);
    }

    public Task<byte[]> MergeAsync(IReadOnlyList<byte[]> documents, CancellationToken cancellationToken = default) => Task.FromResult(Pdf);
}
