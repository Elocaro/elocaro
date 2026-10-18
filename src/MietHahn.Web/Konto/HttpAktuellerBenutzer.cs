using MietHahn.Application.Gemeinsam;

namespace MietHahn.Web.Konto;

/// <summary>Angemeldeter Benutzer aus dem HTTP-Kontext; ohne Anfrage (Hintergrundjobs) „system“.</summary>
internal sealed class HttpAktuellerBenutzer(IHttpContextAccessor kontext) : IAktuellerBenutzer
{
    public string Kennung =>
        kontext.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : IAktuellerBenutzer.System;
}
