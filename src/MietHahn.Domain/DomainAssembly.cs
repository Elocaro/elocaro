using System.Reflection;

namespace MietHahn.Domain;

/// <summary>Anker für Reflection, z. B. in Architekturtests und bei der DI-Registrierung.</summary>
public static class DomainAssembly
{
    public static Assembly Instance => typeof(DomainAssembly).Assembly;
}
