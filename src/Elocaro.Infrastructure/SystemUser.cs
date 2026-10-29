using Elocaro.Application.Common;

namespace Elocaro.Infrastructure;

/// <summary>Standard ohne angemeldeten Benutzer (Hintergrundjobs, Migrationen, Tests).</summary>
internal sealed class SystemUser : ICurrentUser
{
    public string Identifier => ICurrentUser.System;
}
