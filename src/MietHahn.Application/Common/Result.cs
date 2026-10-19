namespace MietHahn.Application.Common;

/// <summary>Outcome of a command from the UI. <see cref="Errors"/> are phrased for users.</summary>
public sealed record Result(bool Succeeded, IReadOnlyList<string> Errors, Guid? Id = null)
{
    public static Result Ok(Guid? id = null) => new(true, [], id);

    public static Result Failed(params string[] errors) => new(false, errors);
}
