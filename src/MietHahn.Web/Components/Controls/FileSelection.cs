namespace MietHahn.Web.Components.Controls;

/// <summary>A file chosen by the user; the stream is only opened when the page reads it.</summary>
public sealed record FileSelection(string Name, long Length, string ContentType, Func<Stream> Open);
