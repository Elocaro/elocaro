using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Elocaro.Infrastructure.Receipts.Extraction;

/// <summary>Log messages of the providers. Deliberately without document content, prompts or keys.</summary>
internal static partial class ExtractionLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} not reachable ({Reason}).")]
    public static partial void NotReachable(ILogger logger, string provider, HttpRequestError reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} did not answer in time.")]
    public static partial void TimedOut(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} answered with status {Status}.")]
    public static partial void Rejected(ILogger logger, string provider, int status);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} extraction finished ({Input} input and {Output} output tokens).")]
    public static partial void Finished(ILogger logger, string provider, int input, int output);
}
