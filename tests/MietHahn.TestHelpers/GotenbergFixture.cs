using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MietHahn.TestHelpers;

/// <summary>Gotenberg (Chromium) for real PDF generation in tests.</summary>
public sealed class GotenbergFixture : IAsyncLifetime
{
    public const string Image = "gotenberg/gotenberg:8.38.1";
    private const int Port = 3000;

    private readonly IContainer container = new ContainerBuilder(Image)
        .WithPortBinding(Port, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Port).ForPath("/health")))
        .Build();

    public string Url => $"http://{container.Hostname}:{container.GetMappedPublicPort(Port)}/";

    public async ValueTask InitializeAsync() => await container.StartAsync();

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}
