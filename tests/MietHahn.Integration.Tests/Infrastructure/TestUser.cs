using MietHahn.Application.Common;

namespace MietHahn.Integration.Tests.Infrastructure;

internal sealed class TestUser(string identifier = "test") : ICurrentUser
{
    public string Identifier { get; set; } = identifier;
}
