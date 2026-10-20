using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace MietHahn.Infrastructure.Templates;

/// <summary>
/// Renders Scriban templates for HTML output (emails, PDFs).
/// <list type="bullet">
/// <item>All output is HTML-escaped (protection against markup injected via tenant data), except <see cref="RawHtml"/>.</item>
/// <item>Model properties are available in snake_case (<c>TenantName</c> → <c>tenant_name</c>).</item>
/// <item>Unknown variables are an error (<see cref="TemplateContext.StrictVariables"/>).</item>
/// <item>Helper functions: <c>money</c>, <c>date</c>, <c>number</c>, <c>text_to_html</c> (preserves line breaks).</item>
/// </list>
/// </summary>
public sealed class ScribanRenderer
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private readonly ConcurrentDictionary<string, Template> cache = new(StringComparer.Ordinal);

    /// <summary>Renders a template embedded in the Infrastructure assembly (relative path, e.g. <c>Mail/Templates/test.sbn</c>).</summary>
    public TemplateResult RenderEmbedded(string path, object? model, IReadOnlyDictionary<string, object?>? supplement = null)
    {
        var template = cache.GetOrAdd(path, p => Parse(EmbeddedResource.ReadText(p), p));
        return Render(template, model, supplement);
    }

    /// <summary>Renders template text (e.g. from the database, maintained by the user).</summary>
    /// <param name="snippets">Texts that the template can include with <c>include 'Name'</c> (name → text); only these can be included.</param>
    public static TemplateResult RenderText(string templateText, object? model, IReadOnlyDictionary<string, object?>? supplement = null, string name = "template", IReadOnlyDictionary<string, string>? snippets = null) =>
        Render(Parse(templateText, name), model, supplement, snippets);

    /// <summary>Checks a template for syntax errors without rendering it.</summary>
    public static IReadOnlyList<string> Validate(string templateText)
    {
        var template = Template.Parse(templateText);
        return template.HasErrors ? template.Messages.Select(m => m.ToString()).ToList() : [];
    }

    private static Template Parse(string text, string name)
    {
        var template = Template.Parse(text, name);
        if (template.HasErrors)
        {
            throw new TemplateErrorException($"Vorlage '{name}' enthält Fehler: {string.Join("; ", template.Messages)}");
        }

        return template;
    }

    private static TemplateResult Render(Template template, object? model, IReadOnlyDictionary<string, object?>? supplement, IReadOnlyDictionary<string, string>? snippets = null)
    {
        var global = new ScriptObject();
        HelperFunctions(global);
        if (model is not null)
        {
            global.Import(model);
        }

        if (supplement is not null)
        {
            foreach (var (key, value) in supplement)
            {
                global[key] = value;
            }
        }

        var context = new EscapingTemplateContext { StrictVariables = true, LoopLimit = 10_000, TemplateLoader = new SnippetLoader(snippets ?? new Dictionary<string, string>()) };
        context.PushCulture(German);
        context.PushGlobal(global);

        try
        {
            var html = template.Render(context);
            return new TemplateResult(html, global);
        }
        catch (Scriban.Syntax.ScriptRuntimeException ex) when (ex.InnerException is TemplateErrorException inner)
        {
            throw new TemplateErrorException(inner.Message, ex);
        }
        catch (Scriban.Syntax.ScriptRuntimeException ex)
        {
            throw new TemplateErrorException($"Vorlage '{template.SourceFilePath}' konnte nicht gerendert werden: {ex.OriginalMessage}", ex);
        }
    }

    private static void HelperFunctions(ScriptObject global)
    {
        global.Import("money", new Func<decimal?, string>(Formatting.Money));
        global.Import("date", new Func<object?, string>(Formatting.Date));
        global.Import("number", new Func<decimal?, int, string>((value, decimals) => value?.ToString("N" + decimals, German) ?? ""));
        global.Import("text_to_html", new Func<string?, RawHtml>(text =>
            new RawHtml(Html.Escape(text ?? "").ReplaceLineEndings("<br>"))));
    }

    /// <summary>Resolves <c>include</c> only against the given snippets: no file or database access from a template.</summary>
    private sealed class SnippetLoader(IReadOnlyDictionary<string, string> snippets) : ITemplateLoader
    {
        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName) => templateName;

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath) =>
            snippets.TryGetValue(templatePath, out var text) ? text : throw new TemplateErrorException($"Der Baustein „{templatePath}“ existiert nicht.");

        public ValueTask<string?> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath) => ValueTask.FromResult<string?>(Load(context, callerSpan, templatePath));
    }

    /// <summary>Escapes every expression output, except <see cref="RawHtml"/>.</summary>
    private sealed class EscapingTemplateContext : TemplateContext
    {
        public override TemplateContext Write(SourceSpan span, object? textAsObject) =>
            textAsObject switch
            {
                null => this,
                RawHtml raw => Write(raw.Html),
                _ => Write(Html.Escape(ObjectToString(textAsObject, false))),
            };
    }
}

/// <summary>Result of a render, including the variables set in the template via <c>capture</c>.</summary>
public sealed class TemplateResult(string html, ScriptObject variables)
{
    public string Html { get; } = html;

    /// <summary>
    /// Reads a variable set in the template as plain text (e.g. <c>{{ capture subject }}…{{ end }}</c>).
    /// Because the output was escaped, it is decoded again here.
    /// </summary>
    public string? Variable(string name) =>
        variables.TryGetValue(name, out var value) && value is not null
            ? WebUtility.HtmlDecode(value.ToString())?.Trim()
            : null;
}

public sealed class TemplateErrorException : Exception
{
    public TemplateErrorException()
    {
    }

    public TemplateErrorException(string message)
        : base(message)
    {
    }

    public TemplateErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Escapes only the characters that are critical in HTML text and attributes. Umlauts and other Unicode characters are
/// kept readable (documents are UTF-8).
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
        foreach (var chars in text)
        {
            sb.Append(chars switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => chars.ToString(),
            });
        }

        return sb.ToString();
    }
}

internal static class EmbeddedResource
{
    private static readonly Assembly Assembly = typeof(EmbeddedResource).Assembly;

    /// <summary>Reads an embedded resource by its path in the project (separated by <c>/</c>).</summary>
    public static string ReadText(string path)
    {
        var name = Assembly.GetName().Name + "." + path.Replace('/', '.').Replace('\\', '.');
        using var stream = Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Eingebettete Ressource '{name}' nicht gefunden.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
