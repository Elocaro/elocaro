using System.Globalization;
using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;

namespace Elocaro.Web.Components.Common;

/// <summary>What the entry of a backup target (<c>BackupTargetFields</c>, <c>BackupTargetDialog</c>) needs to know without a browser: names, checks, mapping and the sign-in at Google.</summary>
public static class BackupTargetForm
{
    /// <summary>Short name for a new target of this provider (it can be changed).</summary>
    public static string DefaultName(BackupProvider provider) => provider switch
    {
        BackupProvider.GoogleDrive => "Google Drive",
        BackupProvider.S3 => "S3-Speicher",
        BackupProvider.Azure => "Azure",
        BackupProvider.Sftp => "NAS",
        _ => "Dieser Server",
    };

    /// <summary>German name of a weekday ("Montag").</summary>
    public static string WeekdayName(DayOfWeek day) => CultureInfo.GetCultureInfo("de-DE").DateTimeFormat.GetDayName(day);

    /// <summary>"montags": for the schedule text ("wöchentlich montags um 02:30").</summary>
    public static string WeekdayPlural(DayOfWeek day) => WeekdayName(day).ToLowerInvariant() + "s";

    /// <summary>Address at Google with the code already filled in (what the QR code holds).</summary>
    public static string DeviceLink(DeviceAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var separator = authorization.VerificationUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{authorization.VerificationUrl}{separator}user_code={Uri.EscapeDataString(authorization.UserCode)}";
    }

    /// <summary>Entry values of a saved target (the secrets stay out, an empty field keeps them).</summary>
    public static BackupSettingsInput ToInput(BackupSettingsView target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new BackupSettingsInput
        {
            Name = target.Name,
            Provider = target.Provider,
            Location = target.Location,
            Enabled = target.Enabled,
            Time = target.Time,
            IntervalDays = target.IntervalDays,
            Weekday = target.Weekday ?? DayOfWeek.Monday,
            KeepDaily = target.KeepDaily,
            KeepWeekly = target.KeepWeekly,
            KeepMonthly = target.KeepMonthly,
        };
    }

    /// <summary>What is still missing before the target can be tested or saved (German names of the fields). A stored secret counts as entered.</summary>
    public static IReadOnlyList<string> Missing(BackupSettingsInput data, bool hasPassword, IReadOnlySet<string> storedCredentials)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(storedCredentials);
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(data.Name))
        {
            list.Add("Name");
        }

        var credentials = new Dictionary<string, string>(data.Credentials, StringComparer.Ordinal);
        foreach (var key in storedCredentials.Where(k => string.IsNullOrWhiteSpace(credentials.GetValueOrDefault(k))))
        {
            credentials[key] = "gespeichert";
        }

        var password = string.IsNullOrEmpty(data.Password) && hasPassword ? "gespeichert" : data.Password;
        foreach (var message in BackupProviders.Validate(data.Provider, data.Location, credentials, password))
        {
            var name = message.EndsWith(" fehlt.", StringComparison.Ordinal) ? message[..^" fehlt.".Length] : message;
            list.Add(name.StartsWith("Das ", StringComparison.Ordinal) || name.StartsWith("Der ", StringComparison.Ordinal) ? name[4..] : name);
        }

        if (data.KeepDaily < 0 || data.KeepWeekly < 0 || data.KeepMonthly < 0 || data.KeepDaily + data.KeepWeekly + data.KeepMonthly == 0)
        {
            list.Add("Aufbewahrung (mindestens eine Frist)");
        }

        return list;
    }

    /// <summary>Asks Google at the pace it names until the person confirmed, refused or the code ran out. <c>Credentials</c> is set on success, else <c>Error</c>.</summary>
    public static async Task<GoogleSignInOutcome> WaitForSignInAsync(IGoogleDriveAuthorization google, TimeProvider clock, DeviceAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(google);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(authorization);
        var interval = authorization.IntervalSeconds;
        var deadline = clock.GetUtcNow().AddSeconds(authorization.ExpiresInSeconds);
        while (clock.GetUtcNow() < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
            var result = await google.PollAsync(authorization.DeviceCode, cancellationToken);
            switch (result.State)
            {
                case DeviceAuthorizationState.Pending:
                    continue;
                case DeviceAuthorizationState.SlowDown:
                    interval += 5;
                    continue;
                case DeviceAuthorizationState.Authorized when result.Credentials is not null:
                    return new GoogleSignInOutcome(result.Credentials, null);
                default:
                    return new GoogleSignInOutcome(null, result.Message ?? "Die Anmeldung bei Google ist fehlgeschlagen.");
            }
        }

        return new GoogleSignInOutcome(null, "Der Code ist abgelaufen. Bitte erneut anmelden.");
    }
}

/// <summary>End of the sign-in at Google: the credentials, or why there are none.</summary>
public sealed record GoogleSignInOutcome(IReadOnlyDictionary<string, string>? Credentials, string? Error);
