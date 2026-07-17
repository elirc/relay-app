using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Auth;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connections;
using Relay.Api.Contracts.Runs;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Security;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// The authentication + authorization denial matrix: unauthenticated → 401,
/// foreign workspace → 404, insufficient role → 403, and the allowed paths.
/// A second workspace (Beta) with an Admin and a Member is seeded per test.
/// </summary>
public sealed class AuthApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;

    private static readonly Guid BetaWorkspaceId = new("22222222-0000-0000-0000-0000000000b1");
    private static readonly Guid BetaConnectionId = new("44444444-0000-0000-0000-0000000000b1");
    private static readonly Guid BetaFlowId = new("55555555-0000-0000-0000-0000000000b1");
    private const string BetaAdmin = "admin@beta.test";
    private const string BetaMember = "member@beta.test";
    private const string Password = "password123";

    public AuthApiTests(RelayApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        await _factory.WithDbAsync(async db =>
        {
            if (await db.FindAsync<Workspace>(BetaWorkspaceId) is not null) return;

            var now = DateTimeOffset.UtcNow;
            db.Workspaces.Add(new Workspace { Id = BetaWorkspaceId, Name = "Beta LLC", Slug = "beta", CreatedAtUtc = now });
            db.Users.AddRange(
                new User
                {
                    Id = Guid.NewGuid(), WorkspaceId = BetaWorkspaceId, Email = BetaAdmin,
                    DisplayName = "Beta Admin", PasswordHash = PasswordHasher.Hash(Password),
                    Role = WorkspaceRole.Admin, CreatedAtUtc = now,
                },
                new User
                {
                    Id = Guid.NewGuid(), WorkspaceId = BetaWorkspaceId, Email = BetaMember,
                    DisplayName = "Beta Member", PasswordHash = PasswordHasher.Hash(Password),
                    Role = WorkspaceRole.Member, CreatedAtUtc = now,
                });
            db.Connections.Add(new Connection
            {
                Id = BetaConnectionId, WorkspaceId = BetaWorkspaceId, ConnectorId = DatabaseSeeder.SlackConnectorId,
                Name = "Beta Slack", ConfigJson = "{}", Status = ConnectionStatus.Active,
                CreatedAtUtc = now, UpdatedAtUtc = now,
            });
            db.Flows.Add(new Flow
            {
                Id = BetaFlowId, WorkspaceId = BetaWorkspaceId, Name = "Beta flow",
                TriggerConnectionId = BetaConnectionId, IsEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now,
                Steps = { new FlowStep { Id = Guid.NewGuid(), Order = 0, Name = "Post", ConnectionId = BetaConnectionId, Action = "send_message", ConfigJson = "{}" } },
            });
            await db.SaveChangesAsync();
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> ClientFor(string email)
    {
        var client = _factory.CreateClient();
        await _factory.AuthenticateAsync(client, email, Password);
        return client;
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenAndProfile()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "owner@acme.test", password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options);
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
        Assert.Equal(WorkspaceRole.Admin, body.User.Role);
        Assert.Equal("acme", body.User.WorkspaceSlug);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "owner@acme.test", password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "ghost@acme.test", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_NoToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/workspaces/{DatabaseSeeder.DemoWorkspaceId}/connections");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsAuthenticatedUser()
    {
        using var client = await ClientFor("owner@acme.test");
        var me = await client.GetFromJsonAsync<AuthUserDto>("/api/auth/me", TestJson.Options);
        Assert.Equal("owner@acme.test", me!.Email);
        Assert.Equal(DatabaseSeeder.DemoWorkspaceId, me.WorkspaceId);
    }

    [Fact]
    public async Task ForeignWorkspace_Returns404_NotForbidden()
    {
        // A Beta user reaching into Acme must see 404 (absent), not 403.
        using var client = await ClientFor(BetaMember);
        var response = await client.GetAsync($"/api/workspaces/{DatabaseSeeder.DemoWorkspaceId}/connections");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForeignWorkspaceInDirectory_Returns404()
    {
        using var client = await ClientFor(BetaMember);
        var response = await client.GetAsync($"/api/workspaces/{DatabaseSeeder.DemoWorkspaceId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WorkspaceList_ScopedToCaller()
    {
        using var client = await ClientFor(BetaMember);
        var list = await client.GetFromJsonAsync<List<Relay.Api.Contracts.Workspaces.WorkspaceDto>>(
            "/api/workspaces", TestJson.Options);
        Assert.Single(list!);
        Assert.Equal("beta", list![0].Slug);
    }

    [Fact]
    public async Task MemberRole_MutatingConnection_Returns403()
    {
        using var client = await ClientFor(BetaMember);
        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{BetaWorkspaceId}/connections",
            new CreateConnectionRequest(DatabaseSeeder.SlackConnectorId, "Nope", "{}", null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminRole_MutatingConnection_Returns201()
    {
        using var client = await ClientFor(BetaAdmin);
        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{BetaWorkspaceId}/connections",
            new CreateConnectionRequest(DatabaseSeeder.SlackConnectorId, "Beta email", "{}", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task MemberRole_CanReadConnections()
    {
        using var client = await ClientFor(BetaMember);
        var page = await client.GetFromJsonAsync<PagedResult<ConnectionDto>>(
            $"/api/workspaces/{BetaWorkspaceId}/connections", TestJson.Options);
        Assert.Contains(page!.Items, c => c.Name == "Beta Slack");
    }

    [Fact]
    public async Task MemberRole_CanRunFlow()
    {
        // Running is an operator action allowed to Members.
        using var client = await ClientFor(BetaMember);
        var response = await client.PostAsJsonAsync(
            $"/api/workspaces/{BetaWorkspaceId}/flows/{BetaFlowId}/run", new TriggerRunRequest(null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<RunDetailDto>(TestJson.Options);
        Assert.Equal(RunStatus.Succeeded, run!.Status);
    }
}
