using System.Reflection;

namespace MietHahn.Infrastructure;

/// <summary>Anker für Reflection, z. B. in Architekturtests und bei der DI-Registrierung.</summary>
public static class InfrastructureAssembly
{
    public static Assembly Instance => typeof(InfrastructureAssembly).Assembly;
}
