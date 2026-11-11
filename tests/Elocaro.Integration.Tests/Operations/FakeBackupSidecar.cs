using System.Net;
using System.Text;

namespace Elocaro.Integration.Tests.Operations;

/// <summary>Stands in for the backup container: records requests and answers from a script.</summary>
internal sealed class FakeBackupSidecar : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = [];

    /// <summary>Answers by path (e.g. <c>/backup</c>); anything else: 204.</summary>
    public Dictionary<string, Func<HttpResponseMessage>> Script { get; } = new(StringComparer.Ordinal);

    public bool Offline { get; set; }

    public IEnumerable<string> Paths => Requests.Select(r => r.Path);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline)
        {
            throw new HttpRequestException("connection refused");
        }

        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add((request.Method, path, body));
        Assert.Equal("Bearer test-token", request.Headers.Authorization?.ToString());
        return Script.TryGetValue(path, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NoContent);
    }
}
