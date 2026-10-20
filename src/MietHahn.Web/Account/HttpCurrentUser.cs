using System.Security.Claims;
using MietHahn.Application.Common;

namespace MietHahn.Web.Account;

/// <summary>
/// Signed-in user from the HTTP request, or from the circuit in interactive components; without either
/// (background jobs) "system".
/// </summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor context, CircuitUser circuit) : ICurrentUser
{
    public string Identifier =>
        (context.HttpContext?.User ?? circuit.Principal)?.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : ICurrentUser.System;
}
