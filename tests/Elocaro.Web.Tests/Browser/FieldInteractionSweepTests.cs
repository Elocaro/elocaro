using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Elocaro.Web.Tests.Browser;

/// <summary>
/// Operates every field of every page the way a person does (mouse and keyboard, calendar, clock, selection lists, boxes) with the data of the
/// page sweeps and reports each field that does not take the value over. Nothing is saved: only fields are touched, no button that stores or deletes.
/// </summary>
public abstract class FieldSweepBase(PostgresFixture postgres) : PageSweepBase(postgres)
{
    protected async Task FieldsTakeTheirValuesAsync(int part)
    {
        var page = await SignedInAsAdminAsync(mobile: false);
        await SeedAsync();
        var problems = new List<string>();
        var current = "";
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.Contains("ERR_NETWORK_CHANGED", StringComparison.Ordinal))
            {
                problems.Add($"{current}: Konsole: {message.Text}");
            }
        };
        page.PageError += (_, error) => problems.Add($"{current}: Skriptfehler: {error}");
        var checkedFields = 0;
        var stats = new Dictionary<string, int>();

        foreach (var path in Routes().Concat(OptionalRoutes()).Where((_, index) => index % Parts == part))
        {
            current = path;
            var response = await page.GotoAsync(BaseUrl + path);
            if (response?.Status != 200)
            {
                continue; // loading problems are the business of the page sweeps
            }

            try
            {
                await page.Locator("[data-interactive=true]").WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 3000 });
            }
            catch (TimeoutException)
            {
                // static page: its fields are plain inputs
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            checkedFields += await CheckPageAsync(page, path, problems, stats);
            checkedFields += await CheckDialogsAsync(page, path, problems, stats);
        }

        Console.WriteLine($"FELDER Teil {part + 1}: " + string.Join(", ", stats.Select(s => $"{s.Key} {s.Value}")));
        Assert.True(checkedFields > 0, "Keine Felder gefunden: Der Durchlauf prüft nichts.");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct()));
    }

    private static async Task<int> CheckPageAsync(IPage page, string path, List<string> problems, Dictionary<string, int> stats, string scope = "main")
    {
        var count = 0;
        foreach (var (kind, selector, operate) in Kinds)
        {
            var n = await CheckAsync(page, path, problems, selector.Replace("$S", scope, StringComparison.Ordinal), operate);
            stats[kind] = stats.GetValueOrDefault(kind) + n;
            count += n;
        }

        return count;
    }

    private static readonly (string Kind, string Selector, Func<IPage, ILocator, Task<string?>> Operate)[] Kinds =
    [
        ("Auswahllisten", "$S .mud-input-control.mud-select", SelectAsync),
        ("Kalender und Uhren", "$S .mud-picker", PickerAsync),
        ("Kontrollkästchen", "$S label.mud-checkbox", CheckboxAsync),
        ("Schalter", "$S label.mud-switch", SwitchAsync),
        ("Optionsgruppen", "$S .mud-radio-group", RadioAsync),
        ("Textfelder", "$S .mud-input-control input.mud-input-slot, $S .mud-input-control textarea.mud-input-slot", TypeAsync),
    ];

    private static async Task<int> CheckAsync(IPage page, string path, List<string> problems, string selector, Func<IPage, ILocator, Task<string?>> operate)
    {
        var total = await page.Locator(selector).CountAsync();
        var done = 0;
        for (var index = 0; index < total; index++)
        {
            var control = page.Locator(selector).Nth(index);
            string? problem;
            var before = page.Url;
            try
            {
                if (!await control.IsVisibleAsync() || await control.IsDisabledAsync(new() { Timeout = 1000 }))
                {
                    continue;
                }

                problem = await operate(page, control);
                done++;
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                problem = $"Ausnahme bei {selector}[{index}]: " + string.Join(" ", ex.Message.Split('\n').Take(18).Select(l => l.Trim()));
            }

            if (problem is not null)
            {
                var label = await Describe(control);
                problems.Add($"{path}: {label}: {problem}");
            }

            // A control that navigates (year filter, tab) leaves the page: go back and carry on with the rest.
            if (page.Url != before)
            {
                await page.GotoAsync(before);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            await CloseFloatingAsync(page);
        }

        return done;
    }

    private static readonly Regex Openers = new(
        @"hinzufügen|erfassen|bearbeiten|ändern|eintragen|tauschen|ablesen|Mahnsperre|Gutschrift|Staffel|Vergleichswohnung|Schlüsselart|Brennwert|Eigentümer|Unterart|Profil|Fläche|Zimmer|Zähler|anlegen|Neuer |Neue |Kaution|Indexwert|Weitere Aktionen",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Forbidden = new(@"löschen|entfernen|widerruf|versend|senden|freigeb|abschließ|zurücksetz|exportier|herunter|kündig|ablehn|import", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Opens every dialog the page offers (buttons that add or change something), operates its fields and closes it without saving.</summary>
    private static async Task<int> CheckDialogsAsync(IPage page, string path, List<string> problems, Dictionary<string, int> stats)
    {
        var done = 0;
        var tried = new HashSet<string>();
        const string Buttons = "main button.mud-button-root, main button.mud-icon-button";
        var total = Math.Min(await page.Locator(Buttons).CountAsync(), 40);
        for (var index = 0; index < total; index++)
        {
            var button = page.Locator(Buttons).Nth(index);
            string name;
            try
            {
                if (!await button.IsVisibleAsync() || await button.IsDisabledAsync())
                {
                    continue;
                }

                name = ((await button.InnerTextAsync()) + " " + (await button.GetAttributeAsync("aria-label"))).Trim();
            }
            catch (PlaywrightException)
            {
                continue;
            }

            if (name.Length == 0 || !Openers.IsMatch(name) || Forbidden.IsMatch(name) || !tried.Add(name))
            {
                continue;
            }

            if (name.Contains("Weitere Aktionen", StringComparison.Ordinal))
            {
                done += await CheckMenuAsync(page, path, problems, stats, button);
            }
            else
            {
                done += await OpenDialogAsync(page, path, problems, stats, () => button.ClickAsync(new() { Timeout = 5000 }), name);
            }
        }

        return done;
    }

    private static async Task<int> CheckMenuAsync(IPage page, string path, List<string> problems, Dictionary<string, int> stats, ILocator menuButton)
    {
        await menuButton.ClickAsync(new() { Timeout = 5000 });
        var items = (await page.GetByRole(AriaRole.Menuitem).AllInnerTextsAsync()).Select(t => t.Trim()).Where(t => Openers.IsMatch(t) && !Forbidden.IsMatch(t)).ToList();
        await CloseFloatingAsync(page);
        var done = 0;
        foreach (var item in items)
        {
            done += await OpenDialogAsync(
                page,
                path,
                problems,
                stats,
                async () =>
                {
                    await menuButton.ClickAsync(new() { Timeout = 5000 });
                    await page.GetByRole(AriaRole.Menuitem, new() { Name = item }).First.ClickAsync(new() { Timeout = 5000 });
                },
                item);
        }

        return done;
    }

    private static async Task<int> OpenDialogAsync(IPage page, string path, List<string> problems, Dictionary<string, int> stats, Func<Task> open, string name)
    {
        var before = page.Url;
        var dialog = page.Locator(".mud-dialog");
        try
        {
            await open();
            await dialog.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2500 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            if (page.Url != before)
            {
                await page.GotoAsync(before);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }

            return 0; // no dialog: the button did something else (inline editing, a link, a menu)
        }

        await page.WaitForTimeoutAsync(300);
        stats["Dialoge"] = stats.GetValueOrDefault("Dialoge") + 1;
        await CheckPageAsync(page, $"{path} [Dialog {name.ReplaceLineEndings(" ")}]", problems, stats, ".mud-dialog");

        var cancel = dialog.First.GetByRole(AriaRole.Button, new() { Name = "Abbrechen" });
        if (await dialog.CountAsync() > 0)
        {
            if (await cancel.CountAsync() > 0)
            {
                await cancel.First.ClickAsync(new() { Timeout = 5000 });
            }
            else
            {
                await page.Keyboard.PressAsync("Escape");
            }
        }

        try
        {
            await Assertions.Expect(dialog).ToHaveCountAsync(0, new() { Timeout = 4000 });
        }
        catch (PlaywrightException)
        {
            problems.Add($"{path} [Dialog {name.ReplaceLineEndings(" ")}]: Dialog lässt sich nicht schließen");
            await page.GotoAsync(before);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        return 1;
    }

    // Lists, calendars and menus close by a click beside them: Escape would close a dialog behind them as well.
    private static async Task CloseFloatingAsync(IPage page)
    {
        for (var attempt = 0; attempt < 3 && await page.Locator(".mud-popover-open").CountAsync() > 0; attempt++)
        {
            await page.Mouse.ClickAsync(2, 2);
            await page.WaitForTimeoutAsync(200);
        }
    }

    private static async Task<string> Describe(ILocator control)
    {
        try
        {
            var label = await control.EvaluateAsync<string>("e => { const c = e.closest('.mud-input-control') || e; const l = c.querySelector('label'); return (l ? l.textContent : (e.getAttribute('aria-label') || e.textContent || e.className)).trim().slice(0, 50); }", null, new() { Timeout = 1000 });
            return label;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return "?";
        }
    }

    private static async Task<string?> SelectAsync(IPage page, ILocator select)
    {
        var input = select.Locator("input").First;
        var before = await input.InputValueAsync();
        await CloseFloatingAsync(page);
        await select.Locator(".mud-input").First.ClickAsync(new() { Timeout = 5000 });
        var items = page.Locator(".mud-popover-open .mud-list-item:not(.mud-disabled)");
        try
        {
            await Assertions.Expect(page.Locator(".mud-popover-open")).ToHaveCountAsync(1, new() { Timeout = 4000 });
            await Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item").First).ToBeVisibleAsync(new() { Timeout = 2000 });
        }
        catch (PlaywrightException)
        {
            return $"Liste öffnet sich nicht (offene Popover: {await page.Locator(".mud-popover-open").CountAsync()})";
        }

        if (await items.CountAsync() == 0)
        {
            await CloseFloatingAsync(page);
            return null;
        }

        var texts = await items.AllInnerTextsAsync();
        var target = Array.FindLastIndex([.. texts], t => t.Trim() != before.Trim() && t.Trim().Length > 0 && t.Trim() != "–" && !t.Contains("wählen", StringComparison.Ordinal) && !t.Contains("niemand", StringComparison.Ordinal));
        if (target < 0)
        {
            await CloseFloatingAsync(page);
            return null;
        }

        var chosen = texts[target].Trim();
        await items.Nth(target).ClickAsync(new() { Timeout = 5000 });
        if (page.IsClosed)
        {
            return null;
        }

        try
        {
            await Assertions.Expect(page.Locator(".mud-popover-open")).ToHaveCountAsync(0, new() { Timeout = 4000 });
        }
        catch (PlaywrightException)
        {
            return "Auswahlliste bleibt nach der Wahl offen";
        }

        try
        {
            await Assertions.Expect(input).ToHaveValueAsync(new Regex(@"^\s*" + Regex.Escape(chosen) + @"\s*$"), new() { Timeout = 4000 });
            return null;
        }
        catch (PlaywrightException)
        {
            return $"gewählt „{chosen}“, im Feld steht „{await input.InputValueAsync()}“";
        }
    }

    private static async Task<string?> PickerAsync(IPage page, ILocator picker)
    {
        // The field is found again by its label: after a choice the picker is created anew (new key), and the position among the pickers of the page can shift while its list is open.
        var label = (await picker.Locator("label").First.InnerTextAsync(new() { Timeout = 2000 })).Trim();
        picker = page.Locator(".app-field").Filter(new() { HasText = label }).First;
        var input = picker.Locator("input").First;
        var before = await input.InputValueAsync();
        await CloseFloatingAsync(page);
        if (await input.IsDisabledAsync(new() { Timeout = 1000 }))
        {
            return null; // a date that cannot be changed (for example the date of a reading that is being edited)
        }

        var open = picker.GetByRole(AriaRole.Button, new() { Name = "Open" });
        if (await open.CountAsync() == 0)
        {
            return null;
        }

        await open.First.ClickAsync(new() { Timeout = 5000 });
        string? problem = null;
        string expectedPattern;
        var kind = "?";
        var popover = page.Locator(".mud-popover-open");
        await Assertions.Expect(popover).ToHaveCountAsync(1, new() { Timeout = 4000 });
        try
        {
            await popover.Locator(".mud-picker-calendar, .mud-picker-month, .mud-picker-time-container, .mud-time-picker-hour").First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
        }
        catch (TimeoutException)
        {
            // reported below as not recognised
        }

        if (await popover.Locator(".mud-picker-calendar").CountAsync() > 0)
        {
            kind = "Kalender";
            var days = popover.Locator("button.mud-picker-calendar-day:not(.mud-day-disabled):not(.mud-picker-calendar-day-outside)");
            var texts = await days.AllInnerTextsAsync();
            var wanted = texts.Any(t => t.Trim() == "15") && !before.StartsWith("15.", StringComparison.Ordinal) ? "15" : texts.FirstOrDefault(t => !before.StartsWith(t.Trim().PadLeft(2, '0') + ".", StringComparison.Ordinal))?.Trim();
            if (wanted is null)
            {
                await CloseFloatingAsync(page);
                return null;
            }

            await days.Filter(new() { HasTextRegex = new Regex($@"^\s*{wanted}\s*$") }).First.ClickAsync(new() { Timeout = 5000 });
            expectedPattern = $@"^{wanted.PadLeft(2, '0')}\.\d{{2}}\.\d{{4}}$";
        }
        else if (await popover.Locator(".mud-picker-month").CountAsync() > 0)
        {
            kind = "Monat";
            var months = popover.Locator(".mud-picker-month:not(.mud-picker-month-disabled)");
            await months.Last.ClickAsync(new() { Timeout = 5000 });
            expectedPattern = @"^\d{2}\.\d{4}$";
        }
        else if (await popover.Locator(".mud-picker-time-container, .mud-time-picker-hour").CountAsync() > 0)
        {
            kind = "Uhr";
            // The numbers of the clock are no click targets of their own: a click on the face of the clock chooses the hour, then the minute (as a person does).
            var face = (await popover.Locator(".mud-picker-time-clock-mask").First.BoundingBoxAsync())!;
            await page.Mouse.ClickAsync(face.X + (face.Width * 0.8f), face.Y + (face.Height * 0.5f));
            await page.WaitForTimeoutAsync(500);
            await page.Mouse.ClickAsync(face.X + (face.Width * 0.5f), face.Y + (face.Height * 0.8f));
            var ok = popover.GetByRole(AriaRole.Button, new() { Name = "OK", Exact = true });
            if (await ok.CountAsync() > 0)
            {
                await ok.First.ClickAsync(new() { Timeout = 5000 });
            }

            expectedPattern = @"^\d{2}:\d{2}$";
        }
        else
        {
            await CloseFloatingAsync(page);
            return "Kalender oder Uhr ließ sich nicht erkennen";
        }

        try
        {
            await Assertions.Expect(page.Locator(".mud-popover-open")).ToHaveCountAsync(0, new() { Timeout = 4000 });
        }
        catch (PlaywrightException)
        {
            problem = "Auswahl bleibt nach der Wahl offen";
        }

        try
        {
            await Assertions.Expect(input).ToHaveValueAsync(new Regex(expectedPattern), new() { Timeout = 4000 });
        }
        catch (PlaywrightException)
        {
            problem = $"{problem}{(problem is null ? "" : "; ")}im Feld „{label}“ ({kind}) steht „{await input.InputValueAsync()}“ (erwartet {expectedPattern})";
        }

        return problem;
    }

    private static async Task<string?> CheckboxAsync(IPage page, ILocator box)
    {
        var input = box.Locator("input[type=checkbox]").First;
        var before = await input.IsCheckedAsync();
        await box.ClickAsync(new() { Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        var after = await input.IsCheckedAsync();
        if (after == before)
        {
            return $"Klick ändert den Zustand nicht (bleibt {(before ? "an" : "aus")})";
        }

        await box.ClickAsync(new() { Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        return await input.IsCheckedAsync() == before ? null : "zweiter Klick stellt den Zustand nicht zurück";
    }

    private static Task<string?> SwitchAsync(IPage page, ILocator toggle) => CheckboxAsync(page, toggle);

    private static async Task<string?> RadioAsync(IPage page, ILocator group)
    {
        var radios = group.Locator("label.mud-radio");
        var total = await radios.CountAsync();
        if (total < 2)
        {
            return null;
        }

        var target = radios.Nth(total - 1);
        var input = target.Locator("input[type=radio]").First;
        if (await input.IsCheckedAsync())
        {
            target = radios.Nth(0);
            input = target.Locator("input[type=radio]").First;
        }

        await target.ClickAsync(new() { Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);
        return await input.IsCheckedAsync() ? null : "Klick wählt die Option nicht aus";
    }

    private static async Task<string?> TypeAsync(IPage page, ILocator field)
    {
        var type = (await field.GetAttributeAsync("type")) ?? "text";
        if (type is "password" or "file" or "hidden" or "checkbox" or "radio" or "search" || await field.GetAttributeAsync("readonly") is not null)
        {
            return null;
        }

        if (await field.EvaluateAsync<bool>("e => !!e.closest('.mud-picker, .mud-select, .mud-autocomplete')"))
        {
            return null; // the text of those is the business of the picker or the list
        }

        var numeric = (await field.GetAttributeAsync("inputmode")) is "decimal" or "numeric" || type == "number";
        var typed = numeric ? "12" : "Prüfung";
        await field.ClickAsync(new() { Timeout = 5000 });
        await field.FillAsync(typed);
        await field.PressAsync("Tab");
        await page.WaitForTimeoutAsync(100);
        var after = await field.InputValueAsync();
        if (after.Length == 0)
        {
            return $"„{typed}“ wurde nicht übernommen (Feld leer)";
        }

        return null;
    }
}

public class FieldSweepPart1(PostgresFixture postgres) : FieldSweepBase(postgres)
{
    [Fact]
    public Task Fields_take_their_values() => FieldsTakeTheirValuesAsync(0);
}

public class FieldSweepPart2(PostgresFixture postgres) : FieldSweepBase(postgres)
{
    [Fact]
    public Task Fields_take_their_values() => FieldsTakeTheirValuesAsync(1);
}

public class FieldSweepPart3(PostgresFixture postgres) : FieldSweepBase(postgres)
{
    [Fact]
    public Task Fields_take_their_values() => FieldsTakeTheirValuesAsync(2);
}

public class FieldSweepPart4(PostgresFixture postgres) : FieldSweepBase(postgres)
{
    [Fact]
    public Task Fields_take_their_values() => FieldsTakeTheirValuesAsync(3);
}

public class FieldSweepPart5(PostgresFixture postgres) : FieldSweepBase(postgres)
{
    [Fact]
    public Task Fields_take_their_values() => FieldsTakeTheirValuesAsync(4);
}

public class FieldSweepPart6(PostgresFixture postgres) : FieldSweepBase(postgres)
{
    [Fact]
    public Task Fields_take_their_values() => FieldsTakeTheirValuesAsync(5);
}
