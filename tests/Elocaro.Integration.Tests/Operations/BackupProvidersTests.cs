using Elocaro.Application.Operations;
using Elocaro.Domain.Operations;
using Elocaro.Infrastructure.Operations;

namespace Elocaro.Integration.Tests.Operations;

public class BackupProvidersTests
{
    private static readonly BackupRetention Retention = new(7, 4, 12);

    private static Dictionary<string, string> Credentials(params (string Key, string Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

    [Fact]
    public void Google_drive_becomes_an_rclone_remote_configured_through_the_environment_only()
    {
        var target = BackupProviders.Build(BackupProvider.GoogleDrive, "Elocaro-Sicherung",
            Credentials(("RCLONE_CONFIG_GDRIVE_TOKEN", "{\"access_token\":\"x\"}"), ("RCLONE_CONFIG_GDRIVE_CLIENT_ID", "id"), ("AWS_ACCESS_KEY_ID", "fremd")), "pw", Retention);

        Assert.Equal("rclone:gdrive:Elocaro-Sicherung", target.Repository);
        Assert.Equal("drive", target.Environment["RCLONE_CONFIG_GDRIVE_TYPE"]);
        Assert.Equal("drive.file", target.Environment["RCLONE_CONFIG_GDRIVE_SCOPE"]);
        Assert.Equal("id", target.Environment["RCLONE_CONFIG_GDRIVE_CLIENT_ID"]);
        // Only the fields of the provider are passed on
        Assert.DoesNotContain("AWS_ACCESS_KEY_ID", target.Environment.Keys);
    }

    [Fact]
    public void S3_and_azure_targets()
    {
        var s3 = BackupProviders.Build(BackupProvider.S3, " https://s3.example.com/bucket/elocaro ", Credentials(("AWS_ACCESS_KEY_ID", "a"), ("AWS_SECRET_ACCESS_KEY", "b")), "pw", Retention);
        var azure = BackupProviders.Build(BackupProvider.Azure, "elocaro-backup", Credentials(("AZURE_ACCOUNT_NAME", "n"), ("AZURE_ACCOUNT_KEY", "k")), "pw", Retention);

        Assert.Equal("s3:https://s3.example.com/bucket/elocaro", s3.Repository);
        Assert.Equal("azure:elocaro-backup:/", azure.Repository);
    }

    [Fact]
    public void Local_and_sftp_need_no_credentials()
    {
        Assert.Equal("/repo", BackupProviders.Build(BackupProvider.Local, string.Empty, Credentials(), "pw", Retention).Repository);
        Assert.Equal("sftp:backup@nas:/elocaro", BackupProviders.Build(BackupProvider.Sftp, "backup@nas:/elocaro", Credentials(), "pw", Retention).Repository);
        Assert.Empty(BackupProviders.Validate(BackupProvider.Sftp, "backup@nas:/elocaro", Credentials(), "pw"));
    }

    [Fact]
    public void Validation_names_what_is_missing()
    {
        var errors = BackupProviders.Validate(BackupProvider.S3, string.Empty, Credentials(("AWS_ACCESS_KEY_ID", "a")), null);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("Passwort", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Geheimer Schlüssel", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Elocaro:Sicherung")]
    [InlineData("a\nb")]
    [InlineData("$(rm)")]
    public void Google_drive_folder_names_are_restricted(string folder) =>
        Assert.Contains(BackupProviders.Validate(BackupProvider.GoogleDrive, folder, Credentials(("RCLONE_CONFIG_GDRIVE_TOKEN", "t")), "pw"), e => e.Contains("Ordnername", StringComparison.Ordinal));

    [Fact]
    public void Every_provider_has_a_label_and_secrets_are_marked() =>
        Assert.All(BackupProviders.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(BackupProviders.Label(p)));
            Assert.All(BackupProviders.Fields(p).Where(f => f.Key.Contains("SECRET", StringComparison.Ordinal) || f.Key.EndsWith("_KEY", StringComparison.Ordinal) || f.Key.EndsWith("_TOKEN", StringComparison.Ordinal)), f => Assert.True(f.Secret, f.Key));
        });
}

public class BackupScheduleTests
{
    private static readonly TimeOnly Time = new(2, 30);

    private static DateTimeOffset Utc(string text) => DateTimeOffset.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Before_the_time_of_day_nothing_is_due() =>
        Assert.False(BackupSchedule.IsDue(Utc("2026-03-01T01:00:00Z"), Time, 1, Utc("2026-02-28T01:31:00Z"))); // 02:00 local, last backup yesterday

    [Fact]
    public void After_the_time_of_day_a_missing_backup_is_due() =>
        Assert.True(BackupSchedule.IsDue(Utc("2026-03-01T01:35:00Z"), Time, 1, Utc("2026-02-28T01:31:00Z"))); // 02:35 local

    [Fact]
    public void Once_tried_today_it_is_not_due_again_even_if_it_failed() =>
        Assert.False(BackupSchedule.IsDue(Utc("2026-03-01T10:00:00Z"), Time, 1, Utc("2026-03-01T01:45:00Z")));

    [Fact]
    public void Never_backed_up_means_due_as_soon_as_the_time_has_passed() =>
        Assert.True(BackupSchedule.IsDue(Utc("2026-03-01T13:00:00Z"), Time, 1, null));

    [Fact]
    public void A_missed_backup_is_caught_up_later_the_same_day() =>
        Assert.True(BackupSchedule.IsDue(Utc("2026-03-01T20:00:00Z"), Time, 1, Utc("2026-02-28T01:31:00Z")));

    [Fact]
    public void The_hour_skipped_by_daylight_saving_time_is_due_an_hour_later()
    {
        // 2026-03-29: 02:30 does not exist in Berlin, so 03:30 CEST = 01:30 UTC
        Assert.False(BackupSchedule.IsDue(Utc("2026-03-29T01:00:00Z"), Time, 1, Utc("2026-03-28T01:31:00Z")));
        Assert.True(BackupSchedule.IsDue(Utc("2026-03-29T01:31:00Z"), Time, 1, Utc("2026-03-28T01:31:00Z")));
    }

    [Fact]
    public void A_weekly_target_waits_for_the_seventh_day()
    {
        var lastBackup = Utc("2026-03-01T01:35:00Z"); // Sunday 02:35 local

        Assert.False(BackupSchedule.IsDue(Utc("2026-03-02T10:00:00Z"), Time, 7, lastBackup));
        Assert.False(BackupSchedule.IsDue(Utc("2026-03-07T10:00:00Z"), Time, 7, lastBackup));
        Assert.True(BackupSchedule.IsDue(Utc("2026-03-08T01:31:00Z"), Time, 7, lastBackup));
    }

    [Fact]
    public void A_weekly_target_that_never_ran_is_due_at_once() =>
        Assert.True(BackupSchedule.IsDue(Utc("2026-03-01T13:00:00Z"), Time, 7, null));

    [Theory]
    [InlineData("azure:elocaro-backup:/", BackupProvider.Azure, "elocaro-backup")]
    [InlineData("s3:https://s3.example.de/bucket/elocaro", BackupProvider.S3, "https://s3.example.de/bucket/elocaro")]
    [InlineData("sftp:backup@nas.example.de:/volume1/elocaro", BackupProvider.Sftp, "backup@nas.example.de:/volume1/elocaro")]
    [InlineData("rclone:gdrive:Elocaro-Sicherung", BackupProvider.GoogleDrive, "Elocaro-Sicherung")]
    [InlineData("/repo", BackupProvider.Local, "")]
    public void The_repository_address_of_the_server_configuration_is_split_into_provider_and_place(string repository, BackupProvider provider, string location)
    {
        Assert.Equal((provider, location), BackupProviders.Parse(repository));
    }

    [Theory]
    [InlineData("")]
    [InlineData("rest:https://host/repo")]
    [InlineData("b2:bucket:path")]
    public void An_address_without_a_provider_in_the_app_is_not_taken_over(string repository)
    {
        Assert.Null(BackupProviders.Parse(repository));
    }
}
