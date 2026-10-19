namespace MietHahn.Application.Common;

/// <summary>Who is currently acting, e.g. for audit fields. Background jobs provide <see cref="System"/>.</summary>
public interface ICurrentUser
{
    /// <summary>Identifier for audit purposes (username or <see cref="System"/>).</summary>
    string Identifier { get; }

    public const string System = "system";
}
