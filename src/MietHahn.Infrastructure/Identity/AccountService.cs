using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MietHahn.Application.Accounts;
using MietHahn.Application.Audit;
using MietHahn.Application.Communication;
using MietHahn.Infrastructure.Audit;
using MietHahn.Infrastructure.Persistence;

namespace MietHahn.Infrastructure.Identity;

internal sealed class AccountService(
    MietHahnDbContext db,
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

    public async Task<AccountResult> InviteAsync(string email, string displayName, string role, CancellationToken cancellationToken = default)
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
        if (role == Roles.Manager)
        {
            await userManager.AddClaimsAsync(user, Permissions.ManagerDefault.Select(r => new System.Security.Claims.Claim(Permissions.ClaimType, r.ToString())));
        }

        if (Roles.RequiresPassword(role))
        {
            var token = await userManager.GenerateUserTokenAsync(user, TokenProviders.Invitation, TokenProviders.Invitation);
            var link = app.Value.Link($"konto/einladung?benutzer={user.Id}&code={Uri.EscapeDataString(token)}");
            await mail.EnqueueAsync(new MailRequest([email], "invitation-password",
                new { DisplayName = user.DisplayName, Role = Roles.DisplayName(role), Link = link.ToString(), ValidDays = InvitationValidity.Days }), cancellationToken);
        }
        else
        {
            var link = await CreateMagicLinkAsync(user.Id, InvitationValidity, cancellationToken);
            await mail.EnqueueAsync(new MailRequest([email], "invitation-link",
                new { DisplayName = user.DisplayName, Role = Roles.DisplayName(role), Link = link.ToString(), ValidDays = InvitationValidity.Days }), cancellationToken);
        }

        await audit.LogAsync(nameof(ApplicationUser), user.Id, AuditAction.Created,
            [new("Email", null, user.Email), new("Rolle", null, role)], cancellationToken);
        return AccountResult.Ok(user.Id);
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
            user.EmailConfirmed = true; // Wer den Link aus der Mail nutzt, hat die Adresse bestätigt.
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null; // gleichzeitig eingelöst
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

    /// <summary>Legt fehlende Rollen an (idempotent).</summary>
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
