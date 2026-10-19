using System.Reflection;

namespace MietHahn.Infrastructure;

/// <summary>Anchor for reflection, e.g. in architecture tests and DI registration.</summary>
public static class InfrastructureAssembly
{
    public static Assembly Instance => typeof(InfrastructureAssembly).Assembly;
}
