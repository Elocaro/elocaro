using System.Reflection;

namespace MietHahn.Application;

/// <summary>Anker für Reflection, z. B. in Architekturtests und bei der DI-Registrierung.</summary>
public static class ApplicationAssembly
{
    public static Assembly Instance => typeof(ApplicationAssembly).Assembly;
}
