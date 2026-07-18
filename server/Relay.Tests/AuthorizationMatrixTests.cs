using System.Net;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Security;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// A parameterized authorization matrix over role × workspace × endpoint. A
/// foreign workspace looks absent (404) before role is even considered; an
/// insufficient role on an owned workspace is forbidden (403). Metrics and
/// dead-letter endpoints are included explicitly.
/// </summary>
public sealed class AuthorizationMatrixTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;

    // Beta workspace ids (string literals so they can appear in InlineData).
    private const string BetaWorkspace = "22222222-0000-0000-0000-0000000000c1";
    private const string BetaFlow = "55555555-0000-0000-0000-0000000000c1";
    private const string BetaConnection = "44444444-0000-0000-0000-0000000000c1";
    private const string BetaAdmin = "admin@matrix.test";
    private const string BetaMember = "member@matrix.test";
    private const string Password = "password123";

    private static readonly Guid AcmeWs = DatabaseSeeder.DemoWorkspaceId;

    public AuthorizationMatrixTests(RelayApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.SeedAsync();
        await _factory.WithDbAsync(async db =>
        {
            if (await db.FindAsync<Workspace>(Guid.Parse(BetaWorkspace)) is not null) return;

            var now = DateTimeOffset.UtcNow;
            db.Workspaces.Add(new Workspace { Id = Guid.Parse(BetaWorkspace), Name = "Matrix LLC", Slug = "matrix", CreatedAtUtc = now });
            db.Users.AddRange(
                new User
                {
                    Id = Guid.NewGuid(), WorkspaceId = Guid.Parse(BetaWorkspace), Email = BetaAdmin,
                    DisplayName = "Matrix Admin", PasswordHash = PasswordHasher.Hash(Password),
                    Role = WorkspaceRole.Admin, CreatedAtUtc = now,
                },
                new User
                {
                    Id = Guid.NewGuid(), WorkspaceId = Guid.Parse(BetaWorkspace), Email = BetaMember,
                    DisplayName = "Matrix Member", PasswordHash = PasswordHasher.Hash(Password),
                    Role = WorkspaceRole.Member, CreatedAtUtc = now,
                });
            db.Connections.Add(new Connection
            {
                Id = Guid.Parse(BetaConnection), WorkspaceId = Guid.Parse(BetaWorkspace),
                ConnectorId = DatabaseSeeder.SlackConnectorId, ConnectorVersionId = DatabaseSeeder.SlackConnectorV1Id,
                Name = "Matrix Slack", ConfigJson = """{"channel":"#m"}""", Status = ConnectionStatus.Active,
                CreatedAtUtc = now, UpdatedAtUtc = now,
            });
            db.Flows.Add(new Flow
            {
                Id = Guid.Parse(BetaFlow), WorkspaceId = Guid.Parse(BetaWorkspace), Name = "Matrix flow",
                TriggerConnectionId = Guid.Parse(BetaConnection), IsEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now,
                Steps = { new FlowStep { Id = Guid.NewGuid(), Order = 0, Name = "Post", ConnectionId = Guid.Parse(BetaConnection), Action = "send_message", ConfigJson = "{}" } },
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

    // ---- foreign workspace → 404 (a Matrix user reaching into Acme) ----

    [Theory]
    [InlineData("connections")]
    [InlineData("flows")]
    [InlineData("runs")]
    [InlineData("dead-letter")]
    [InlineData("metrics")]
    [InlineData("metrics?days=30")]
    public async Task ForeignWorkspace_ReadEndpoints_Return404(string suffix)
    {
        using var client = await ClientFor(BetaMember);
        var response = await client.GetAsync($"/api/workspaces/{AcmeWs}/{suffix}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ForeignWorkspace_FlowMetrics_Return404()
    {
        using var client = await ClientFor(BetaMember);
        var response = await client.GetAsync($"/api/workspaces/{AcmeWs}/flows/{DatabaseSeeder.DemoFlowId}/metrics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- insufficient role → 403 (a Member mutating owned admin-only endpoints) ----

    [Theory]
    [InlineData("connections")]                                   // create connection
    [InlineData("flows")]                                         // create flow
    [InlineData("flows/" + BetaFlow + "/enable")]                 // enable flow
    [InlineData("flows/" + BetaFlow + "/disable")]                // disable flow
    [InlineData("flows/" + BetaFlow + "/webhooks")]               // create webhook
    [InlineData("flows/" + BetaFlow + "/schedules")]              // create schedule
    [InlineData("connections/" + BetaConnection + "/rotate-secret")] // rotate secret
    public async Task MemberRole_AdminOnlyMutations_Return403(string suffix)
    {
        using var client = await ClientFor(BetaMember);
        // The role check runs in the authorization filter, before body binding.
        var response = await client.PostAsync($"/api/workspaces/{BetaWorkspace}/{suffix}", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MemberRole_PublishingConnectorVersion_Return403()
    {
        // Connector versioning is a global admin-only surface (no workspace in route).
        using var client = await ClientFor(BetaMember);
        var response = await client.PostAsync($"/api/connectors/{DatabaseSeeder.SlackConnectorId}/versions/1/deprecate", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- allowed paths: reads are open to Members on their own workspace ----

    [Theory]
    [InlineData("metrics")]
    [InlineData("dead-letter")]
    [InlineData("runs")]
    public async Task MemberRole_OwnWorkspaceReads_Return200(string suffix)
    {
        using var client = await ClientFor(BetaMember);
        var response = await client.GetAsync($"/api/workspaces/{BetaWorkspace}/{suffix}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_MetricsAndDeadLetter_Return401()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/workspaces/{BetaWorkspace}/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/workspaces/{BetaWorkspace}/dead-letter")).StatusCode);
    }
}
