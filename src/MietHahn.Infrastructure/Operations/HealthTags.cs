namespace MietHahn.Infrastructure.Operations;

public static class HealthTags
{
    /// <summary>Dependencies without which the app cannot work (database, Gotenberg).</summary>
    public const string Ready = "bereit";
}
