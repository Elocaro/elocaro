using Microsoft.EntityFrameworkCore;
using MietHahn.Application.Common;
using MietHahn.Domain.Common;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Common;

/// <summary>Führt einen Befehl aus und übersetzt erwartbare Fehler in ein <see cref="Result"/> für die Oberfläche.</summary>
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

/// <summary>Innerhalb von <see cref="Command.ExecuteAsync"/>: der Datensatz existiert nicht.</summary>
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
