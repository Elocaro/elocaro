using MietHahn.Application;
using MietHahn.Domain;
using MietHahn.Infrastructure;
using NetArchTest.Rules;
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace MietHahn.Architecture.Tests;

/// <summary>
/// Checks the dependency direction as defined in docs/architektur.md:
/// Web → Application/Infrastructure, Infrastructure → Application → Domain.
/// </summary>
public class LayerRulesTests
{
    [Fact]
    public void Domain_does_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(DomainAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOnAny("MietHahn.Application", "MietHahn.Infrastructure", "MietHahn.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    [Fact]
    public void Domain_does_not_depend_on_frameworks()
    {
        var result = Types.InAssembly(DomainAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_Web()
    {
        var result = Types.InAssembly(ApplicationAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOnAny("MietHahn.Infrastructure", "MietHahn.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Web()
    {
        var result = Types.InAssembly(InfrastructureAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOn("MietHahn.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, Violations(result));
    }

    private static string Violations(ArchTestResult result) =>
        "Verstöße gegen die Schichtregeln: " + string.Join(", ", result.FailingTypeNames ?? []);
}
