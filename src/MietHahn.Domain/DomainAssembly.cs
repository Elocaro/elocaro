using System.Reflection;

namespace MietHahn.Domain;

/// <summary>Anchor for reflection, e.g. in architecture tests and DI registration.</summary>
public static class DomainAssembly
{
    public static Assembly Instance => typeof(DomainAssembly).Assembly;
}
