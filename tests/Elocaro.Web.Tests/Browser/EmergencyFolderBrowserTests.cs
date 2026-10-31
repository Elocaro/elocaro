using Elocaro.Application.Leases;
using Elocaro.Application.People;
using Elocaro.Application.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>The emergency folder end to end: download from the page, open the file from disk (file://), unlock it in the browser.</summary>
public class EmergencyFolderBrowserTests(PostgresFixture postgres) : BrowserTestBasis(postgres)
{
    private const string FilePassword = "Das-ist-ein-langes-Passwort-2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Downloaded_file_unlocks_from_disk_with_the_password_and_not_without_it_and_runs_no_injected_code()
    {
        await App.WithScopeAsync(async sp =>
        {
            var properties = sp.GetRequiredService<IPropertyService>();
            var house = (await properties.CreateAsync(new PropertyInput { Name = "Lindenweg 3", Street = "Lindenweg 3", PostalCode = "50667", City = "Köln" }, Ct)).Id!.Value;
            var unit = (await properties.CreateUnitAsync(house, new UnitInput { Name = "Wohnung 1" }, Ct)).Id!.Value;
            var tenant = (await sp.GetRequiredService<IPersonService>().CreateAsync(
                new PersonInput { FirstName = "Anna <img src=x onerror=\"parent.__pwned=1;top.__pwned=1\">", LastName = "Schmidt", Salutation = Elocaro.Domain.People.Salutation.Mrs }, null, Ct)).Id!.Value;
            Assert.True((await sp.GetRequiredService<ILeaseService>().CreateAsync(
                new LeaseCreation { RentalUnitId = unit, Start = new DateOnly(2026, 3, 1), TenantIds = [tenant], BaseRent = 620m }, Ct)).Succeeded);
            return 0;
        });

        var admin = await SignedInAsAdminAsync(mobile: true);
        var path = Path.Combine(Path.GetTempPath(), "elocaro-tests", Guid.NewGuid().ToString("N"), "notfallmappe.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await WithScreenshotOnFailureAsync(admin, async () =>
            {
                await admin.GotoAsync(BaseUrl + "/verwaltung/system/notfallmappe");
                await NoHorizontalScrollAsync(admin);
                await admin.GetByLabel("Passwort (mindestens 12 Zeichen)").FillAsync(FilePassword);
                await admin.GetByLabel("Passwort wiederholen").FillAsync(FilePassword);
                var download = await admin.RunAndWaitForDownloadAsync(() => admin.GetByRole(AriaRole.Button, new() { Name = "Notfallmappe erzeugen" }).ClickAsync());
                await download.SaveAsAsync(path);
                Assert.EndsWith(".html", download.SuggestedFilename, StringComparison.Ordinal);
            });

            // A fresh browser context, opened from disk like a relative would do.
            var viewer = await NewPageAsync(mobile: true);
            var problems = new List<string>();
            var dialogs = new List<string>();
            viewer.Console += (_, message) =>
            {
                if (message.Type is "error" or "warning")
                {
                    problems.Add(message.Text);
                }
            };
            viewer.Dialog += async (_, dialog) =>
            {
                dialogs.Add(dialog.Message);
                await dialog.DismissAsync();
            };

            await WithScreenshotOnFailureAsync(viewer, async () =>
            {
                await viewer.GotoAsync(new Uri(path).AbsoluteUri);
                Assert.StartsWith("file:", viewer.Url, StringComparison.Ordinal);
                Assert.True(await viewer.EvaluateAsync<bool>("() => !!(window.crypto && window.crypto.subtle)"), "WebCrypto fehlt unter file://");
                await Assertions.Expect(viewer.GetByRole(AriaRole.Heading, new() { Name = "Elocaro Notfallmappe" })).ToBeVisibleAsync();
                await NoHorizontalScrollAsync(viewer);

                // Wrong password: a clear message, nothing shown.
                await viewer.GetByLabel("Passwort").FillAsync("falsches-aber-langes-Passwort");
                await viewer.GetByRole(AriaRole.Button, new() { Name = "Mappe öffnen" }).ClickAsync();
                await Assertions.Expect(viewer.GetByRole(AriaRole.Alert)).ToContainTextAsync("Das Passwort passt nicht");
                await Assertions.Expect(viewer.Locator("#inhalt")).ToBeHiddenAsync();

                // Right password.
                await viewer.GetByLabel("Passwort").FillAsync(FilePassword);
                await viewer.GetByRole(AriaRole.Button, new() { Name = "Mappe öffnen" }).ClickAsync();
                var folder = viewer.FrameLocator("#mappe-rahmen");
                await Assertions.Expect(folder.GetByRole(AriaRole.Heading, new() { Name = "Erste Schritte: die ersten 30 Tage" })).ToBeVisibleAsync();
                await Assertions.Expect(folder.Locator("body")).ToContainTextAsync("Lindenweg 3");
                await Assertions.Expect(folder.Locator("body")).ToContainTextAsync("Schmidt");
                await Assertions.Expect(folder.Locator("body")).ToContainTextAsync("<img src=x onerror=");
                await Assertions.Expect(viewer.Locator("#sperre")).ToBeHiddenAsync();
                await NoHorizontalScrollAsync(viewer);
                Assert.Equal(0, await folder.Locator("img").CountAsync());
                Assert.Null(await viewer.EvaluateAsync<object?>("() => window.__pwned ?? null"));
            });

            Assert.Empty(dialogs);
            Assert.DoesNotContain(problems, p => !p.Contains("favicon", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
