using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Flows;
using Relay.Api.Contracts.Runs;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Optimistic-concurrency and rate-limiting edges: a stale token loses without
/// partially applying its step replacement, and trigger throttling returns 429 at
/// the boundary while leaving non-trigger routes available.
/// </summary>
public sealed class ConcurrencyExpansionTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private static string FlowBase => $"/api/workspaces/{Ws}/flows";

    public ConcurrencyExpansionTests(RelayApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        await _factory.AuthenticateAsync(_client);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static UpdateFlowRequest Update(string name, Guid? expected, params (string Name, string Action)[] steps) => new(
        name, null, DatabaseSeeder.DemoInboundConnectionId,
        steps.Select(s => new FlowStepInput(s.Name, DatabaseSeeder.DemoSlackConnectionId, s.Action, "{}")).ToList(),
        expected);

    [Fact]
    public async Task StaleToken_LosesStepReplacement_WithoutPartialApply()
    {
        var created = await _client.PostAsJsonAsync(FlowBase, new CreateFlowRequest(
            "Race", null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("Original", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")]));
        var flow = (await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;

        // Writer A wins with the fresh token, replacing the step list with two steps.
        var winning = await _client.PutAsJsonAsync($"{FlowBase}/{flow.Id}",
            Update("Race A", flow.ConcurrencyToken, ("A1", "send_message"), ("A2", "http_request")));
        Assert.Equal(HttpStatusCode.OK, winning.StatusCode);

        // Writer B, holding the now-stale token, is rejected.
        var losing = await _client.PutAsJsonAsync($"{FlowBase}/{flow.Id}",
            Update("Race B", flow.ConcurrencyToken, ("B1", "a"), ("B2", "b"), ("B3", "c")));
        Assert.Equal(HttpStatusCode.Conflict, losing.StatusCode);

        // The persisted flow reflects only writer A — none of B's steps leaked in.
        var reread = await _client.GetFromJsonAsync<FlowDetailDto>($"{FlowBase}/{flow.Id}", TestJson.Options);
        Assert.Equal("Race A", reread!.Name);
        Assert.Equal(2, reread.Steps.Count);
        Assert.Equal(["A1", "A2"], reread.Steps.Select(s => s.Name).ToArray());
        Assert.Equal([0, 1], reread.Steps.Select(s => s.Order).ToArray());
    }

    [Fact]
    public async Task SequentialStepReplacement_NeverViolatesTheUniqueOrderIndex()
    {
        var created = await _client.PostAsJsonAsync(FlowBase, new CreateFlowRequest(
            "Rewrite", null, DatabaseSeeder.DemoInboundConnectionId,
            [new FlowStepInput("S0", DatabaseSeeder.DemoSlackConnectionId, "send_message", "{}")]));
        var flow = (await created.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;
        var token = flow.ConcurrencyToken;

        // Grow to three, then shrink to one — the delete-then-insert transaction must
        // keep the unique (FlowId, Order) index consistent at every step.
        var grow = await _client.PutAsJsonAsync($"{FlowBase}/{flow.Id}",
            Update("Rewrite", token, ("N0", "a"), ("N1", "b"), ("N2", "c")));
        Assert.Equal(HttpStatusCode.OK, grow.StatusCode);
        var grown = (await grow.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;

        var shrink = await _client.PutAsJsonAsync($"{FlowBase}/{flow.Id}",
            Update("Rewrite", grown.ConcurrencyToken, ("Only", "z")));
        Assert.Equal(HttpStatusCode.OK, shrink.StatusCode);
        var shrunk = (await shrink.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options))!;
        Assert.Single(shrunk.Steps);
        Assert.Equal(0, shrunk.Steps[0].Order);
    }

    [Fact]
    public async Task Triggers_ThrottleAt429Boundary_ButLeaveReadsAvailable()
    {
        // A host whose trigger policy permits only two requests per window.
        using var limited = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:TriggerPermitLimit"] = "2",
                })));

        var client = limited.CreateClient();
        await _factory.AuthenticateAsync(client);
        var runUrl = $"{FlowBase}/{DatabaseSeeder.DemoFlowId}/run";

        var first = await client.PostAsJsonAsync(runUrl, new TriggerRunRequest(null));
        var second = await client.PostAsJsonAsync(runUrl, new TriggerRunRequest(null));
        var third = await client.PostAsJsonAsync(runUrl, new TriggerRunRequest(null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);

        // The throttle is scoped to trigger endpoints; reads stay available.
        var reads = await client.GetFromJsonAsync<PagedResult<FlowSummaryDto>>(FlowBase, TestJson.Options);
        Assert.NotNull(reads);
        Assert.Contains(reads!.Items, f => f.Id == DatabaseSeeder.DemoFlowId);
    }
}
