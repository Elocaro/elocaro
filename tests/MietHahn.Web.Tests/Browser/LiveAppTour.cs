using System.Text;
using Microsoft.Playwright;
using MietHahn.TestHelpers;

namespace MietHahn.Web.Tests.Browser;

/// <summary>
/// On demand against a running instance with real data (MIETHAHN_LIVE_URL, credentials from MIETHAHN_LIVE_LOGIN = file with
/// e-mail, password and authenticator key): follows the navigation links read-only, takes screenshots on desktop and smartphone and
/// reports script errors, failed requests, horizontal overflow and slow pages (report.txt in MIETHAHN_TOUR_DIR).
/// </summary>
public class LiveAppTour(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static readonly string[] Skip = ["/dateien", "export", ".pdf", ".csv", ".json", "abmelden", "/konto/", "/api/", "download", "/hangfire", "/quartz"];

    [Fact]
    public async Task Tour_through_the_running_app()
    {
        var url = Environment.GetEnvironmentVariable("MIETHAHN_LIVE_URL");
        var loginFile = Environment.GetEnvironmentVariable("MIETHAHN_LIVE_LOGIN");
        var directory = Environment.GetEnvironmentVariable("MIETHAHN_TOUR_DIR");
        if (url is null || loginFile is null || directory is null)
        {
            Assert.Skip("Nur auf Abruf: MIETHAHN_LIVE_URL, MIETHAHN_LIVE_LOGIN, MIETHAHN_TOUR_DIR");
            return;
        }

        var login = await File.ReadAllLinesAsync(loginFile, TestContext.Current.CancellationToken);
        Directory.CreateDirectory(directory);
        var report = new List<string>();
        var visited = new List<string>();
        var pages = new List<IPage>();
        foreach (var mobile in new[] { false, true })
        {
            var page = await NewPageAsync(mobile);
            pages.Add(page);
            var current = "";
            page.Console += (_, m) => { if (m.Type == "error") { report.Add($"[{(mobile ? "m" : "d")}] {current}: Konsole: {m.Text}"); } };
            page.PageError += (_, e) => report.Add($"[{(mobile ? "m" : "d")}] {current}: Skriptfehler: {e}");
            page.Response += (_, r) => { if (r.Status >= 400 && !r.Url.Contains("favicon", StringComparison.Ordinal)) { report.Add($"[{(mobile ? "m" : "d")}] {current}: HTTP {r.Status} {r.Url}"); } };

            await page.GotoAsync(url + "konto/anmelden");
            await page.FillAsync("#email", login[0]);
            await page.FillAsync("#password", login[1]);
            await page.ClickAsync("button[type=submit]");
            await page.WaitForURLAsync("**/zwei-faktor**");
            await page.FillAsync("#code", Totp.Code(login[2], DateTimeOffset.UtcNow));
            await page.ClickAsync("button[type=submit]");
            await page.WaitForURLAsync(u => !u.Contains("/konto/", StringComparison.Ordinal));

            if (Environment.GetEnvironmentVariable("MIETHAHN_LIVE_TALL") is { } tall)
            {
                await page.SetViewportSizeAsync(mobile ? 360 : 1280, int.Parse(tall, System.Globalization.CultureInfo.InvariantCulture));
            }

            var only = Environment.GetEnvironmentVariable("MIETHAHN_LIVE_ONLY")?.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var queue = new Queue<string>(only is not null ? only : mobile ? visited.ToList() : ["/verwaltung"]);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            while (queue.Count > 0 && index < 160)
            {
                var path = queue.Dequeue();
                if (!seen.Add(path))
                {
                    continue;
                }

                current = path;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var response = await page.GotoAsync(url.TrimEnd('/') + path);
                try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 8000 }); } catch (TimeoutException) { }
                await page.WaitForTimeoutAsync(250);
                watch.Stop();
                if (response?.Status != 200)
                {
                    report.Add($"[{(mobile ? "m" : "d")}] {path}: Status {response?.Status}");
                    continue;
                }

                if (watch.ElapsedMilliseconds > 2500)
                {
                    report.Add($"[{(mobile ? "m" : "d")}] {path}: langsam ({watch.ElapsedMilliseconds} ms)");
                }

                var width = await page.EvaluateAsync<int[]>("() => { const c = document.getElementById('content'); return [Math.max(document.documentElement.scrollWidth, c ? c.scrollWidth : 0), window.innerWidth]; }");
                if (width[0] > width[1])
                {
                    report.Add($"[{(mobile ? "m" : "d")}] {path}: {width[0]}px breit bei {width[1]}px");
                }

                var height = await page.EvaluateAsync<int>("() => { const c = document.querySelector('main.content') ?? document.querySelector('main'); return c ? c.scrollHeight : 0; }");
                var name = new string(path.Trim('/').Replace("verwaltung/", "", StringComparison.Ordinal).Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
                await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{(mobile ? "m" : "d")}{index:000}-{name[..Math.Min(name.Length, 70)]}.png") });
                report.Add($"[{(mobile ? "m" : "d")}] {index:000} {height,6}px {watch.ElapsedMilliseconds,5} ms {path}");
                index++;
                if (!mobile && only is null)
                {
                    visited.Add(path);
                    var hrefs = await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('a[href^=\"/verwaltung\"]')].map(a => a.getAttribute('href'))");
                    foreach (var href in hrefs.Select(h => h.Split('#')[0]).Where(h => h.Length > 0 && !Skip.Any(s => h.Contains(s, StringComparison.OrdinalIgnoreCase))).Distinct())
                    {
                        if (!seen.Contains(href) && !queue.Contains(href))
                        {
                            queue.Enqueue(href);
                        }
                    }
                }
            }
        }

        await File.WriteAllTextAsync(Path.Combine(directory, "report.txt"), string.Join("\n", report), Encoding.UTF8, TestContext.Current.CancellationToken);
    }
}
