namespace MietHahn.Application.Common;

/// <summary>Ausgang eines Befehls aus der Oberfläche. <see cref="Errors"/> sind für Benutzer formuliert.</summary>
public sealed record Result(bool Succeeded, IReadOnlyList<string> Errors, Guid? Id = null)
{
    public static Result Ok(Guid? id = null) => new(true, [], id);

    public static Result Failed(params string[] errors) => new(false, errors);
}
