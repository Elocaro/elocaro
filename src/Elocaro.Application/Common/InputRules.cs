using System.ComponentModel.DataAnnotations;

namespace Elocaro.Application.Common;

/// <summary>
/// The rules written as attributes on the input classes (required, length, range …), checked by the services and not only by the form
/// in the browser: a caller without a form (the MCP interface, an import) has to meet them too.
/// </summary>
public static class InputRules
{
    /// <summary>A failed <see cref="Result"/> with the messages, or <c>null</c> if all inputs are valid.</summary>
    public static Result? Check(params object?[] inputs)
    {
        var messages = new List<string>();
        foreach (var input in inputs.Where(i => i is not null))
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(input!, new ValidationContext(input!), results, validateAllProperties: true))
            {
                messages.AddRange(results.Select(r => r.ErrorMessage ?? "Ungültige Eingabe."));
            }
        }

        return messages.Count == 0 ? null : new Result(false, [.. messages.Distinct()]);
    }
}
