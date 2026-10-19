using System.Reflection;

namespace MietHahn.Application;

/// <summary>Anchor for reflection, e.g. in architecture tests and DI registration.</summary>
public static class ApplicationAssembly
{
    public static Assembly Instance => typeof(ApplicationAssembly).Assembly;
}
