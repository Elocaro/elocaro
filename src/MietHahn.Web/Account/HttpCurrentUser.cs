using MietHahn.Application.Common;

namespace MietHahn.Web.Account;

/// <summary>Angemeldeter Benutzer aus dem HTTP-Kontext; ohne Anfrage (Hintergrundjobs) „system“.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor context) : ICurrentUser
{
    public string Identifier =>
        context.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : ICurrentUser.System;
}
