using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Elocaro.Web.Account;

/// <summary>
/// The signed-in user of an interactive circuit. There is no <c>HttpContext</c> inside a circuit, so the principal is taken
/// from the authentication state when the circuit opens and read by <see cref="HttpCurrentUser"/> (audit fields).
/// </summary>
internal sealed class CircuitUser
{
    public ClaimsPrincipal? Principal { get; set; }
}

/// <summary>Fills <see cref="CircuitUser"/> as soon as a circuit opens.</summary>
internal sealed class CircuitUserHandler(AuthenticationStateProvider authentication, CircuitUser user) : CircuitHandler
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        user.Principal = (await authentication.GetAuthenticationStateAsync()).User;
        authentication.AuthenticationStateChanged += async state => user.Principal = (await state).User;
    }
}
