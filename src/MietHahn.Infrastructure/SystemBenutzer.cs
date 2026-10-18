using MietHahn.Application.Gemeinsam;

namespace MietHahn.Infrastructure;

/// <summary>Standard ohne angemeldeten Benutzer (Hintergrundjobs, Migrationen, Tests).</summary>
internal sealed class SystemBenutzer : IAktuellerBenutzer
{
    public string Kennung => IAktuellerBenutzer.System;
}
