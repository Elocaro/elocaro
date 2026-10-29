using Elocaro.Application.Common;
using Elocaro.Domain.Common;
using Elocaro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Elocaro.Infrastructure.Common;

/// <summary>Executes a command and translates expected errors into a <see cref="Result"/> for the UI.</summary>
internal static class Command
{
    public static Result NotFound => Result.Failed("Der Datensatz wurde nicht gefunden.");

    public static async Task<Result> ExecuteAsync(Func<Task<Guid>> command, string overlap = "Für diesen Zeitraum gibt es bereits einen Stand.")
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            return Result.Ok(await command());
        }
        catch (NotFoundException)
        {
            return NotFound;
        }
        catch (RuleViolationException errors)
        {
            return Result.Failed(errors.Message);
        }
        catch (Exception errors) when (DatabaseErrors.IsOverlap(errors))
        {
            return Result.Failed(overlap);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failed("Der Datensatz wurde zwischenzeitlich geändert. Bitte die Seite neu laden.");
        }
    }
}

/// <summary>Within <see cref="Command.ExecuteAsync"/>: the record does not exist.</summary>
internal sealed class NotFoundException : Exception
{
    public NotFoundException()
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
