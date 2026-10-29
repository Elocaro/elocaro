using Elocaro.Application.Receipts;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>Provider "None": receipts are entered by hand.</summary>
internal sealed class NoReceiptExtractor(IOptions<ReceiptExtractionOptions> options) : IReceiptExtractor
{
    /// <summary>An unknown provider name never activates anything; the configuration error is shown instead.</summary>
    public ExtractorInfo Info => options.Value.ProviderKind is null
        ? new ExtractorInfo(
            "Keiner", null, Enabled: true, Ready: false, SendsToThirdParty: false,
            "Die Belegauslesung ist wegen eines Konfigurationsfehlers ausgeschaltet. Es werden keine Dokumente an Dritte übermittelt.",
            $"Unbekannter Anbieter „{options.Value.Provider}“ (ReceiptExtraction__Provider); erlaubt sind None, Claude und Ollama.")
        : new ExtractorInfo(
            "Keiner", null, Enabled: false, Ready: false, SendsToThirdParty: false,
            "Die automatische Belegauslesung ist ausgeschaltet. Belege werden von Hand erfasst, es werden keine Dokumente an Dritte übermittelt.",
            "Die Belegauslesung ist nicht aktiviert.");

    public Task<ExtractorResult> ExtractAsync(IReadOnlyList<ExtractionDocument> documents, ExtractionContext context, CancellationToken cancellationToken = default) =>
        throw new ExtractionException("Die Belegauslesung ist nicht aktiviert.");
}
