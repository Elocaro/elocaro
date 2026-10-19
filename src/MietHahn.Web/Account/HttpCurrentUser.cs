using MietHahn.Application.Common;

namespace MietHahn.Web.Account;

/// <summary>Signed-in user from the HTTP context; without a request (background jobs) "system".</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor context) : ICurrentUser
{
    public string Identifier =>
        context.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : ICurrentUser.System;
}
