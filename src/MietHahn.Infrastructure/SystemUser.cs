using MietHahn.Application.Common;

namespace MietHahn.Infrastructure;

/// <summary>Standard ohne angemeldeten Benutzer (Hintergrundjobs, Migrationen, Tests).</summary>
internal sealed class SystemUser : ICurrentUser
{
    public string Identifier => ICurrentUser.System;
}
