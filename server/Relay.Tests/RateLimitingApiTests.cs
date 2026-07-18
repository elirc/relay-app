using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Relay.Api.Contracts.Runs;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class RateLimitingApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;

    public RateLimitingApiTests(RelayApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.SeedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Trigger_ExceedingPermitLimit_Returns429()
    {
        // A host variant whose trigger policy permits only one request per window.
        using var limited = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:TriggerPermitLimit"] = "1",
                })));

        var client = limited.CreateClient();
        await _factory.AuthenticateAsync(client);

        var url = $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/run";
        var first = await client.PostAsJsonAsync(url, new TriggerRunRequest(null));
        var second = await client.PostAsJsonAsync(url, new TriggerRunRequest(null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }
}
