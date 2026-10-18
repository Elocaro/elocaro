using MietHahn.Application;
using MietHahn.Domain;
using MietHahn.Infrastructure;
using NetArchTest.Rules;
using ArchTestResult = NetArchTest.Rules.TestResult;

namespace MietHahn.Architecture.Tests;

/// <summary>
/// Prüft die Abhängigkeitsrichtung laut docs/architektur.md:
/// Web → Application/Infrastructure, Infrastructure → Application → Domain.
/// </summary>
public class SchichtregelnTests
{
    [Fact]
    public void Domain_haengt_von_keiner_anderen_Schicht_ab()
    {
        var ergebnis = Types.InAssembly(DomainAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOnAny("MietHahn.Application", "MietHahn.Infrastructure", "MietHahn.Web")
            .GetResult();

        Assert.True(ergebnis.IsSuccessful, Verstoesse(ergebnis));
    }

    [Fact]
    public void Domain_haengt_nicht_von_Frameworks_ab()
    {
        var ergebnis = Types.InAssembly(DomainAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        Assert.True(ergebnis.IsSuccessful, Verstoesse(ergebnis));
    }

    [Fact]
    public void Application_haengt_nicht_von_Infrastructure_oder_Web_ab()
    {
        var ergebnis = Types.InAssembly(ApplicationAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOnAny("MietHahn.Infrastructure", "MietHahn.Web")
            .GetResult();

        Assert.True(ergebnis.IsSuccessful, Verstoesse(ergebnis));
    }

    [Fact]
    public void Infrastructure_haengt_nicht_von_Web_ab()
    {
        var ergebnis = Types.InAssembly(InfrastructureAssembly.Instance)
            .ShouldNot()
            .HaveDependencyOn("MietHahn.Web")
            .GetResult();

        Assert.True(ergebnis.IsSuccessful, Verstoesse(ergebnis));
    }

    private static string Verstoesse(ArchTestResult ergebnis) =>
        "Verstöße gegen die Schichtregeln: " + string.Join(", ", ergebnis.FailingTypeNames ?? []);
}
