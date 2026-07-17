using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Workspaces;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class WorkspacesApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;

    public WorkspacesApiTests(RelayApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.SeedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_ReturnsSeededDemoWorkspace()
    {
        var workspaces = await _client.GetFromJsonAsync<List<WorkspaceDto>>("/api/workspaces", TestJson.Options);

        Assert.NotNull(workspaces);
        Assert.Contains(workspaces!, w => w.Id == DatabaseSeeder.DemoWorkspaceId && w.Slug == "acme");
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/workspaces/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
