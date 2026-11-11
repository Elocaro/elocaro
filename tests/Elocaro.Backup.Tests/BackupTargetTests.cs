using Elocaro.Backup;

namespace Elocaro.Backup.Tests;

public class BackupTargetTests
{
    [Fact]
    public void A_complete_target_is_valid() =>
        Assert.Empty(new BackupTarget("rclone:gdrive:elocaro", "pw", new Dictionary<string, string>(StringComparer.Ordinal) { ["RCLONE_CONFIG_GDRIVE_TYPE"] = "drive", ["AWS_SECRET_ACCESS_KEY"] = "x" }).Validate());

    [Theory]
    [InlineData("", "pw")]
    [InlineData("-o foo", "pw")]
    [InlineData("/repo\nsecond", "pw")]
    [InlineData("/repo", "")]
    public void Repository_and_password_are_required(string repository, string password) =>
        Assert.NotEmpty(new BackupTarget(repository, password).Validate());

    [Theory]
    [InlineData("PATH")]
    [InlineData("LD_PRELOAD")]
    [InlineData("PGPASSWORD")]
    [InlineData("RESTIC_PASSWORD")]
    [InlineData("aws_access_key_id")]
    public void Only_backend_credentials_may_be_passed_on(string variable) =>
        Assert.NotEmpty(new BackupTarget("/repo", "pw", new Dictionary<string, string>(StringComparer.Ordinal) { [variable] = "x" }).Validate());

    [Fact]
    public void Retention_must_keep_something() =>
        Assert.NotEmpty(new BackupTarget("/repo", "pw", null, new RetentionPolicy(0, 0, 0)).Validate());
}
