using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace MietHahn.Infrastructure.Vorlagen;

/// <summary>
/// Rendert Scriban-Vorlagen für HTML-Ausgaben (Mails, PDFs).
/// <list type="bullet">
/// <item>Alle Ausgaben werden HTML-escaped (Schutz vor eingeschleustem Markup aus Mieterdaten), außer <see cref="RohesHtml"/>.</item>
/// <item>Modell-Eigenschaften sind in snake_case verfügbar (<c>MieterName</c> → <c>mieter_name</c>).</item>
/// <item>Unbekannte Variablen sind ein Fehler (<see cref="TemplateContext.StrictVariables"/>).</item>
/// <item>Hilfsfunktionen: <c>geld</c>, <c>datum</c>, <c>zahl</c>, <c>text_zu_html</c> (Zeilenumbrüche erhalten).</item>
/// </list>
/// </summary>
public sealed class ScribanRenderer
{
    private static readonly CultureInfo Deutsch = CultureInfo.GetCultureInfo("de-DE");

    private readonly ConcurrentDictionary<string, Template> cache = new(StringComparer.Ordinal);

    /// <summary>Rendert eine im Infrastructure-Assembly eingebettete Vorlage (Pfad relativ, z. B. <c>Mail/Vorlagen/test.sbn</c>).</summary>
    public VorlagenErgebnis RendernEingebettet(string pfad, object? modell, IReadOnlyDictionary<string, object?>? zusatz = null)
    {
        var vorlage = cache.GetOrAdd(pfad, p => Parsen(EingebetteteRessource.LesenText(p), p));
        return Rendern(vorlage, modell, zusatz);
    }

    /// <summary>Rendert einen Vorlagentext (z. B. aus der Datenbank, vom Benutzer gepflegt).</summary>
    public static VorlagenErgebnis RendernText(string vorlagenText, object? modell, IReadOnlyDictionary<string, object?>? zusatz = null, string name = "vorlage") =>
        Rendern(Parsen(vorlagenText, name), modell, zusatz);

    /// <summary>Prüft eine Vorlage auf Syntaxfehler, ohne sie zu rendern.</summary>
    public static IReadOnlyList<string> Pruefen(string vorlagenText)
    {
        var vorlage = Template.Parse(vorlagenText);
        return vorlage.HasErrors ? vorlage.Messages.Select(m => m.ToString()).ToList() : [];
    }

    private static Template Parsen(string text, string name)
    {
        var vorlage = Template.Parse(text, name);
        if (vorlage.HasErrors)
        {
            throw new VorlagenFehlerException($"Vorlage '{name}' enthält Fehler: {string.Join("; ", vorlage.Messages)}");
        }

        return vorlage;
    }

    private static VorlagenErgebnis Rendern(Template vorlage, object? modell, IReadOnlyDictionary<string, object?>? zusatz)
    {
        var global = new ScriptObject();
        Hilfsfunktionen(global);
        if (modell is not null)
        {
            global.Import(modell);
        }

        if (zusatz is not null)
        {
            foreach (var (schluessel, wert) in zusatz)
            {
                global[schluessel] = wert;
            }
        }

        var kontext = new EscapingTemplateContext { StrictVariables = true, LoopLimit = 10_000 };
        kontext.PushCulture(Deutsch);
        kontext.PushGlobal(global);

        try
        {
            var html = vorlage.Render(kontext);
            return new VorlagenErgebnis(html, global);
        }
        catch (Scriban.Syntax.ScriptRuntimeException ex)
        {
            throw new VorlagenFehlerException($"Vorlage '{vorlage.SourceFilePath}' konnte nicht gerendert werden: {ex.OriginalMessage}", ex);
        }
    }

    private static void Hilfsfunktionen(ScriptObject global)
    {
        global.Import("geld", new Func<decimal?, string>(Formatierung.Geld));
        global.Import("datum", new Func<object?, string>(Formatierung.Datum));
        global.Import("zahl", new Func<decimal?, int, string>((wert, stellen) => wert?.ToString("N" + stellen, Deutsch) ?? ""));
        global.Import("text_zu_html", new Func<string?, RohesHtml>(text =>
            new RohesHtml(Html.Escape(text ?? "").ReplaceLineEndings("<br>"))));
    }

    /// <summary>Escaped jede Ausgabe eines Ausdrucks, außer <see cref="RohesHtml"/>.</summary>
    private sealed class EscapingTemplateContext : TemplateContext
    {
        public override TemplateContext Write(SourceSpan span, object? textAsObject) =>
            textAsObject switch
            {
                null => this,
                RohesHtml roh => Write(roh.Html),
                _ => Write(Html.Escape(ObjectToString(textAsObject, false))),
            };
    }
}

/// <summary>Ergebnis eines Rendervorgangs inklusive der in der Vorlage per <c>capture</c> gesetzten Variablen.</summary>
public sealed class VorlagenErgebnis(string html, ScriptObject variablen)
{
    public string Html { get; } = html;

    /// <summary>
    /// Liest eine in der Vorlage gesetzte Variable als Klartext (z. B. <c>{{ capture betreff }}…{{ end }}</c>).
    /// Da die Ausgabe escaped wurde, wird hier wieder dekodiert.
    /// </summary>
    public string? Variable(string name) =>
        variablen.TryGetValue(name, out var wert) && wert is not null
            ? WebUtility.HtmlDecode(wert.ToString())?.Trim()
            : null;
}

public sealed class VorlagenFehlerException : Exception
{
    public VorlagenFehlerException()
    {
    }

    public VorlagenFehlerException(string message)
        : base(message)
    {
    }

    public VorlagenFehlerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Escaped nur die in HTML-Text und -Attributen kritischen Zeichen. Umlaute und andere Unicode-Zeichen bleiben
/// lesbar erhalten (Dokumente sind UTF-8).
/// </summary>
public static class Html
{
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new System.Text.StringBuilder(text.Length + 16);
        foreach (var zeichen in text)
        {
            sb.Append(zeichen switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => zeichen.ToString(),
            });
        }

        return sb.ToString();
    }
}

internal static class EingebetteteRessource
{
    private static readonly Assembly Assembly = typeof(EingebetteteRessource).Assembly;

    /// <summary>Liest eine eingebettete Ressource über ihren Pfad im Projekt (mit <c>/</c> getrennt).</summary>
    public static string LesenText(string pfad)
    {
        var name = Assembly.GetName().Name + "." + pfad.Replace('/', '.').Replace('\\', '.');
        using var stream = Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Eingebettete Ressource '{name}' nicht gefunden.");
        using var leser = new StreamReader(stream);
        return leser.ReadToEnd();
    }
}
