using Elocaro.Application.Accounts;
using Elocaro.Application.Audit;
using Elocaro.Application.Communication;
using Elocaro.Infrastructure.Audit;
using Elocaro.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Elocaro.Infrastructure.Identity;

internal sealed class AccountService(
    ElocaroDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<Role> roleManager,
    IMailSender mail,
    TimeProvider clock,
    IOptions<ApplicationOptions> app,
    AuditWriter audit) : IAccountService
{
    public static readonly TimeSpan MagicLinkValidity = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(7);

    public async Task<bool> InitialSetupPossibleAsync(CancellationToken cancellationToken = default) =>
        !await db.Users.AnyAsync(cancellationToken);

    public async Task<AccountResult> CreateFirstAdminAsync(string email, string displayName, string password, CancellationToken cancellationToken = default)
    {
        await EnsureRolesAsync(roleManager);
        if (!await InitialSetupPossibleAsync(cancellationToken))
        {
            return AccountResult.Failed("Die Ersteinrichtung ist bereits abgeschlossen.");
        }

        var user = NewUser(email, displayName);
        user.EmailConfirmed = true;
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return Errors(result);
        }

        await userManager.AddToRoleAsync(user, Roles.Admin);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Created,
            [new("Email", null, user.Email), new("Rolle", null, Roles.Admin)], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public Task<AccountResult> InviteAsync(string email, string displayName, string role, CancellationToken cancellationToken = default) =>
        InviteAsync(email, displayName, role, null, cancellationToken);

    public Task<AccountResult> InviteAsync(string email, string displayName, string role, string? invitedBy, CancellationToken cancellationToken = default) =>
        InviteAsync(email, displayName, role, invitedBy, Permissions.ManagerDefault, cancellationToken);

    public async Task<AccountResult> InviteAsync(string email, string displayName, string role, string? invitedBy, IReadOnlyCollection<AccessRight> managerPermissions, CancellationToken cancellationToken = default)
    {
        if (!Roles.All.Contains(role))
        {
            return AccountResult.Failed($"Unbekannte Rolle '{role}'.");
        }

        await EnsureRolesAsync(roleManager);
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return AccountResult.Failed("Für diese E-Mail-Adresse existiert bereits ein Konto.");
        }

        var user = NewUser(email, displayName);
        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            return Errors(result);
        }

        await userManager.AddToRoleAsync(user, role);
        if (role == Roles.Manager && managerPermissions.Count > 0)
        {
            await userManager.AddClaimsAsync(user, managerPermissions.Select(r => new System.Security.Claims.Claim(Permissions.ClaimType, r.ToString())));
        }

        await SendInvitationAsync(user, role, invitedBy, cancellationToken);

        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Created,
            [new("Email", null, user.Email), new("Rolle", null, role)], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    public async Task<AccountResult> ResendInvitationAsync(Guid userId, string? invitedBy = null, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return AccountResult.Failed("Das Konto gibt es nicht.");
        }

        if (user.LastSignInAt is not null)
        {
            return AccountResult.Failed("Die Person hat sich bereits angemeldet. Eine neue Einladung ist nicht nötig; auf der Anmeldeseite lässt sich ein neuer Link anfordern.");
        }

        if (user.Locked)
        {
            return AccountResult.Failed("Das Konto ist gesperrt.");
        }

        var role = (await userManager.GetRolesAsync(user)).FirstOrDefault() ?? "";
        await SendInvitationAsync(user, role, invitedBy, cancellationToken);
        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Modified, [new("Einladung", null, "erneut gesendet")], cancellationToken);
        return AccountResult.Ok(user.Id);
    }

    private async Task SendInvitationAsync(ApplicationUser user, string role, string? invitedBy, CancellationToken cancellationToken)
    {
        object Model(string link, bool password) => new
        {
            DisplayName = user.DisplayName,
            Role = Roles.DisplayName(role),
            Link = link,
            ValidDays = InvitationValidity.Days,
            Landlord = string.IsNullOrWhiteSpace(invitedBy) ? null : invitedBy,
            Password = password,
        };

        if (Roles.RequiresPassword(role))
        {
            var token = await userManager.GenerateUserTokenAsync(user, TokenProviders.Invitation, TokenProviders.Invitation);
            var link = app.Value.Link($"konto/einladung?benutzer={user.Id}&code={Uri.EscapeDataString(token)}");
            await mail.EnqueueAsync(new MailRequest([user.Email!], "invitation-password", Model(link.ToString(), true)), cancellationToken);
        }
        else
        {
            var link = await CreateMagicLinkAsync(user.Id, InvitationValidity, cancellationToken);
            await mail.EnqueueAsync(new MailRequest([user.Email!], "invitation-link", Model(link.ToString(), false)), cancellationToken);
        }
    }

    public async Task RequestMagicLinkAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || user.Locked || !Roles.AllowsMagicLink(await userManager.GetRolesAsync(user)))
        {
            return;
        }

        var link = await CreateMagicLinkAsync(user.Id, MagicLinkValidity, cancellationToken);
        await mail.EnqueueAsync(new MailRequest([user.Email!], "magic-link",
            new { DisplayName = user.DisplayName, Link = link.ToString(), ValidMinutes = (int)MagicLinkValidity.TotalMinutes }), cancellationToken);
    }

    public async Task<Guid?> RedeemMagicLinkAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var hash = MagicLink.Hash(token);
        var link = await db.Set<MagicLink>().SingleOrDefaultAsync(l => l.TokenHash == hash, cancellationToken);
        var now = clock.GetUtcNow();
        if (link is null || !link.IsRedeemable(now))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(link.UserId.ToString());
        if (user is null || user.Locked || !Roles.AllowsMagicLink(await userManager.GetRolesAsync(user)))
        {
            return null;
        }

        link.Redeem(now);
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true; // Using the link from the email confirms the address.
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // redeemed concurrently
        }

        return user.Id;
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null || user.Locked || !await userManager.HasPasswordAsync(user))
        {
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = app.Value.Link($"konto/passwort-zuruecksetzen?benutzer={user.Id}&code={Uri.EscapeDataString(token)}");
        await mail.EnqueueAsync(new MailRequest([user.Email!], "password-reset",
            new { DisplayName = user.DisplayName, Link = link.ToString() }), cancellationToken);
    }

    /// <summary>Creates missing roles (idempotent).</summary>
    public static async Task EnsureRolesAsync(RoleManager<Role> roleManager)
    {
        ArgumentNullException.ThrowIfNull(roleManager);
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new Role(role));
            }
        }
    }

    private ApplicationUser NewUser(string email, string displayName) => new()
    {
        UserName = email.Trim(),
        Email = email.Trim(),
        DisplayName = displayName.Trim(),
        CreatedAt = clock.GetUtcNow(),
    };

    private async Task<Uri> CreateMagicLinkAsync(Guid userId, TimeSpan validity, CancellationToken cancellationToken)
    {
        var (link, token) = MagicLink.Generate(userId, clock.GetUtcNow(), validity);
        db.Add(link);
        await db.SaveChangesAsync(cancellationToken);
        return app.Value.Link($"konto/link/{token}");
    }

    private static AccountResult Errors(IdentityResult result) =>
        AccountResult.Failed([.. result.Errors.Select(e => e.Description)]);
}
