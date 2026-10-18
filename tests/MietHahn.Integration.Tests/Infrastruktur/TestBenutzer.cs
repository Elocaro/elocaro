using MietHahn.Application.Gemeinsam;

namespace MietHahn.Integration.Tests.Infrastruktur;

internal sealed class TestBenutzer(string kennung = "test") : IAktuellerBenutzer
{
    public string Kennung { get; set; } = kennung;
}
