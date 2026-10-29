using Elocaro.Application.Common;

namespace Elocaro.Integration.Tests.Infrastructure;

internal sealed class TestUser(string identifier = "test") : ICurrentUser
{
    public string Identifier { get; set; } = identifier;
}
