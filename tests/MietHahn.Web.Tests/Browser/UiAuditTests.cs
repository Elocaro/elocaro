using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using MietHahn.Application.People;
using MietHahn.Application.Properties;

namespace MietHahn.Web.Tests.Browser;

/// <summary>
/// Looks at the rendered pages as a user would: text contrast (WCAG AA) in light and dark, and an on-demand screenshot tour
/// (<c>MIETHAHN_TOUR=1</c>, optionally <c>MIETHAHN_HEADED=1</c> to watch and <c>MIETHAHN_TOUR_DIR</c> for the target folder).
/// </summary>
public class UiAuditTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private static readonly string[] Pages =
    [
        "/verwaltung", "/verwaltung/personen", "/verwaltung/personen/neu", "/verwaltung/objekte", "/verwaltung/vertraege",
        "/verwaltung/dokumente", "/verwaltung/bank/konten", "/verwaltung/bank/umsaetze", "/verwaltung/finanzen/offene-posten",
        "/verwaltung/finanzen/mahnungen", "/verwaltung/belege", "/verwaltung/benutzer", "/verwaltung/system/jobs",
        "/verwaltung/system/datensicherung", "/konto/sicherheit",
    ];

    // Collects text whose contrast to its effective background is below AA (4.5:1, large text 3:1).
    private const string ContrastScript = """
        () => {
          const parse = s => {
            let m = s.match(/^rgba?\(([^)]+)\)/);
            if (m) { const p = m[1].split(/[ ,\/]+/).filter(Boolean).map(Number); return { r: p[0], g: p[1], b: p[2], a: p.length > 3 ? p[3] : 1 }; }
            m = s.match(/^color\(srgb ([^)]+)\)/);
            if (m) { const p = m[1].split(/[ \/]+/).filter(Boolean).map(Number); return { r: p[0] * 255, g: p[1] * 255, b: p[2] * 255, a: p.length > 3 ? p[3] : 1 }; }
            return null;
          };
          const over = (top, bottom) => { const a = top.a + bottom.a * (1 - top.a); return { r: (top.r * top.a + bottom.r * bottom.a * (1 - top.a)) / a, g: (top.g * top.a + bottom.g * bottom.a * (1 - top.a)) / a, b: (top.b * top.a + bottom.b * bottom.a * (1 - top.a)) / a, a }; };
          const lum = c => { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
          const background = el => {
            const layers = [];
            for (let e = el; e; e = e.parentElement) { const c = parse(getComputedStyle(e).backgroundColor); if (c && c.a > 0) { layers.push(c); if (c.a >= 1) break; } }
            let result = { r: 255, g: 255, b: 255, a: 1 };
            for (const l of layers.reverse()) result = over(l, result);
            return result;
          };
          const failures = [];
          const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
          for (let n = walker.nextNode(); n; n = walker.nextNode()) {
            const text = n.textContent.trim(); const el = n.parentElement;
            if (!text || !el || el.closest('script,style,option,[disabled],[hidden]')) continue;
            const rect = el.getBoundingClientRect(); const style = getComputedStyle(el);
            if (rect.width === 0 || rect.height === 0 || style.visibility === 'hidden' || style.display === 'none') continue;
            const fg = parse(style.color); if (!fg) continue;
            const bg = background(el); const fgOnBg = over({ ...fg, a: fg.a * (parseFloat(style.opacity) || 1) }, bg);
            const l1 = lum(fgOnBg), l2 = lum(bg); const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
            const size = parseFloat(style.fontSize), bold = parseInt(style.fontWeight) >= 700;
            const need = size >= 24 || (size >= 18.66 && bold) ? 3 : 4.5;
            if (ratio < need) failures.push(`${ratio.toFixed(2)}<${need} "${text.slice(0, 40)}" <${el.tagName.toLowerCase()}${el.className && typeof el.className === 'string' ? '.' + el.className.split(' ').join('.') : ''}>`);
          }
          return failures;
        }
        """;

    [Fact]
    public async Task All_areas_fit_360_pixels_without_horizontal_scrolling()
    {
        var property = await App.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<IPropertyService>().CreateAsync(
                new PropertyInput { Name = "Lindenweg 3 mit einem langen Namen", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, TestContext.Current.CancellationToken)).Id!.Value);
        var page = await SignedInAsAdminAsync();
        var problems = new List<string>();
        string[] more =
        [
            "/verwaltung/vpi", "/verwaltung/datenschutz", "/verwaltung/bank/zuordnung", "/verwaltung/bank/regeln", "/verwaltung/bank/csv-profile",
            "/verwaltung/finanzen/darlehen", "/verwaltung/finanzen/anlage-v-zeilen", "/verwaltung/finanzen/einstellungen", "/verwaltung/einstellungen/vermieter",
            "/verwaltung/system/mails", "/verwaltung/system/belegauslesung", "/verwaltung/belege/neu", "/verwaltung/dokumente/neu",
            $"/verwaltung/objekte/{property}", $"/verwaltung/objekte/{property}/anlage-v", $"/verwaltung/objekte/{property}/afa",
            $"/verwaltung/objekte/{property}/erhaltungsaufwand", $"/verwaltung/objekte/{property}/abrechnungen",
        ];
        foreach (var path in Pages.Concat(more))
        {
            var response = await page.GotoAsync(BaseUrl + path);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            if (response?.Status != 200)
            {
                problems.Add($"{path}: Status {response?.Status}");
                continue;
            }

            var width = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth, window.innerWidth]");
            if (width[0] > width[1])
            {
                var culprits = await page.EvaluateAsync<string[]>("() => [...document.body.querySelectorAll('*')].filter(e => e.getBoundingClientRect().right > window.innerWidth + 1 && !e.closest('.table-wrapper,.mud-table-container')).slice(0, 4).map(e => e.tagName.toLowerCase() + (typeof e.className === 'string' && e.className ? '.' + e.className.split(' ').join('.') : '') + ' ' + Math.round(e.getBoundingClientRect().right))");
                problems.Add($"{path}: {width[0]}px breit, Fenster {width[1]}px; Ursache: {string.Join(", ", culprits)}");
            }
        }

        Assert.True(problems.Count == 0, "Horizontales Scrollen bei 360 px:\n" + string.Join("\n", problems));
    }

    [Fact]
    public async Task Text_contrast_meets_AA_in_light_and_dark()
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        var problems = new List<string>();
        foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await page.EmulateMediaAsync(new() { ColorScheme = scheme });
            foreach (var path in Pages)
            {
                await page.GotoAsync(BaseUrl + path);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                problems.AddRange((await page.EvaluateAsync<string[]>(ContrastScript)).Select(f => $"[{scheme}] {path}: {f}"));
            }
        }

        Assert.True(problems.Count == 0, "Kontrast unter WCAG AA:\n" + string.Join("\n", problems.Distinct()));
    }

    [Fact]
    public async Task Screenshot_tour()
    {
        if (Environment.GetEnvironmentVariable("MIETHAHN_TOUR") is null)
        {
            Assert.Skip("Tour nur auf Abruf: MIETHAHN_TOUR=1 (MIETHAHN_HEADED=1 zum Zuschauen)");
        }

        var directory = Environment.GetEnvironmentVariable("MIETHAHN_TOUR_DIR") ?? Path.Combine(Path.GetTempPath(), "miethahn-tour");
        Directory.CreateDirectory(directory);
        var property = await App.WithScopeAsync(async sp =>
        {
            var ct = TestContext.Current.CancellationToken;
            var people = sp.GetRequiredService<IPersonService>();
            await people.CreateAsync(new PersonInput { FirstName = "Anna", LastName = "Schmidt", Email = "anna@example.de" }, null, ct);
            await people.CreateAsync(new PersonInput { FirstName = "Ben", LastName = "Weber" }, null, ct);
            var properties = sp.GetRequiredService<IPropertyService>();
            var id = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, ct)).Id!.Value;
            await properties.CreateUnitAsync(id, new UnitInput { Name = "Wohnung 1", Location = "EG links" }, ct);
            return id;
        });

        var page = await SignedInAsAdminAsync(mobile: false);
        foreach (var mobile in new[] { false, true })
        {
            await page.SetViewportSizeAsync(mobile ? 360 : 1280, mobile ? 780 : 900);
            foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
            {
                await page.EmulateMediaAsync(new() { ColorScheme = scheme });
                foreach (var path in Pages.Append($"/verwaltung/objekte/{property}"))
                {
                    await page.GotoAsync(BaseUrl + path);
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    var name = $"{(mobile ? "mobil" : "desktop")}-{scheme.ToString().ToLowerInvariant()}-{path.Trim('/').Replace('/', '_')}";
                    await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name[..Math.Min(name.Length, 80)] + ".png"), FullPage = true });
                }
            }
        }
    }
}
