using Elocaro.Backup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elocaro.Backup.Tests;

internal sealed class BackupTestEnvironment : IDisposable
{
    public BackupTestEnvironment()
    {
        Root = Path.Combine(Path.GetTempPath(), "elocaro-backup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "data"));
        Settings = new BackupSettings("secret-token", Path.Combine(Root, "data"), Path.Combine(Root, "work"), Path.Combine(Root, "restore"),
            Path.Combine(Root, "status", "backup.json"), "elocaro", "elocaro", 1654, "02:30", new RetentionPolicy(), null, Path.Combine(Root, "export"));
        Engine = new BackupEngine(Runner, Settings, NullLogger<BackupEngine>.Instance);
    }

    public string Root { get; }

    public BackupSettings Settings { get; }

    public FakeProcessRunner Runner { get; } = new();

    public BackupEngine Engine { get; }

    public static BackupTarget Target { get; } = new("/repo", "pw", new Dictionary<string, string>(StringComparer.Ordinal) { ["AWS_ACCESS_KEY_ID"] = "key" });

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
