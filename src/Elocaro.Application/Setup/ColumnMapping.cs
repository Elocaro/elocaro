namespace Elocaro.Application.Setup;

/// <summary>A column of the import template that a source column can be assigned to.</summary>
/// <param name="Key">Stable name, the target of a mapping.</param>
/// <param name="Label">Column heading of the template.</param>
public sealed record ImportColumn(string Key, string Label, bool Required);

/// <param name="Index">Position in the file, counted from 0.</param>
/// <param name="Samples">The first non-empty values, only for the person mapping in the browser (never sent anywhere).</param>
public sealed record SourceColumn(int Index, string Header, IReadOnlyList<string> Samples);

/// <summary>What the import found in a file: its columns, which ones it recognized by name, and which required ones are still missing.</summary>
/// <param name="Recognized">Source column index → template column key, for the headings the import knows.</param>
public sealed record ColumnAnalysis(
    IReadOnlyList<SourceColumn> Source,
    IReadOnlyList<ImportColumn> Target,
    IReadOnlyDictionary<int, string> Recognized,
    IReadOnlyList<ImportColumn> Missing,
    IReadOnlyList<string> FileErrors)
{
    public bool NeedsMapping => Missing.Count > 0;
}
