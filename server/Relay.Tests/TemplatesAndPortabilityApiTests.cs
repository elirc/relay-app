using System.Net;
using System.Net.Http.Json;
using Relay.Api.Contracts.Flows;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class TemplatesAndPortabilityApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;

    private static readonly Guid SlackTemplateId = new("88888888-0000-0000-0000-000000000001");
    private static readonly Guid SheetsTemplateId = new("88888888-0000-0000-0000-000000000002");

    public TemplatesAndPortabilityApiTests(RelayApiFactory factory)
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

    [Fact]
    public async Task ListTemplates_ReturnsSeededGallery()
    {
        var templates = await _client.GetFromJsonAsync<List<FlowTemplateDto>>("/api/flow-templates", TestJson.Options);

        Assert.NotNull(templates);
        Assert.True(templates!.Count >= 3);
        var slack = Assert.Single(templates, t => t.Id == SlackTemplateId);
        Assert.Equal("http", slack.TriggerConnectorKey);
        Assert.Single(slack.Steps);
        Assert.Equal("slack", slack.Steps[0].ConnectorKey);
    }

    [Fact]
    public async Task Instantiate_MappableTemplate_CreatesDisabledDraft()
    {
        var response = await _client.PostAsync(
            $"/api/workspaces/{Ws}/flows/from-template/{SlackTemplateId}", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var flow = await response.Content.ReadFromJsonAsync<FlowDetailDto>(TestJson.Options);
        Assert.False(flow!.IsEnabled);
        Assert.Equal("Slack alert on webhook", flow.Name);
        Assert.Single(flow.Steps);
    }

    [Fact]
    public async Task Instantiate_TemplateWithoutMatchingConnection_Returns400()
    {
        // The demo workspace has no "sheets" connection.
        var response = await _client.PostAsync(
            $"/api/workspaces/{Ws}/flows/from-template/{SheetsTemplateId}", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Instantiate_UnknownTemplate_Returns404()
    {
        var response = await _client.PostAsync(
            $"/api/workspaces/{Ws}/flows/from-template/{Guid.NewGuid()}", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_ReturnsPortableDocument()
    {
        var export = await _client.GetFromJsonAsync<FlowExportDto>(
            $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/export", TestJson.Options);

        Assert.NotNull(export);
        Assert.Equal("http", export!.Trigger.ConnectorKey);
        Assert.Equal("Inbound webhook source", export.Trigger.ConnectionName);
        Assert.Single(export.Steps);
        Assert.Equal("slack", export.Steps[0].ConnectorKey);
        Assert.False(string.IsNullOrWhiteSpace(export.ExternalId));
    }

    [Fact]
    public async Task Import_DryRun_ReportsCreate_WithoutPersisting()
    {
        var export = await _client.GetFromJsonAsync<FlowExportDto>(
            $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/export", TestJson.Options);
        var doc = export! with { ExternalId = "dry-run-ext-1", Name = "Dry run copy" };

        var result = await (await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=true", doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);

        Assert.True(result!.Valid);
        Assert.Equal("create", result.Action);
        Assert.Null(result.FlowId);

        // Nothing was persisted with that external id.
        var again = await (await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=true", doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.Equal("create", again!.Action);
    }

    [Fact]
    public async Task Import_IsIdempotent_ByExternalId()
    {
        var export = await _client.GetFromJsonAsync<FlowExportDto>(
            $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/export", TestJson.Options);
        var doc = export! with { ExternalId = "idem-ext-1", Name = "Imported flow" };

        var first = await (await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=false", doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.Equal("create", first!.Action);
        Assert.NotNull(first.FlowId);

        var second = await (await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=false", doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.Equal("update", second!.Action);
        Assert.Equal(first.FlowId, second.FlowId); // same flow, no duplicate
    }

    [Fact]
    public async Task Import_UnresolvableConnector_IsInvalid()
    {
        var doc = new FlowExportDto(
            "bad-ext-1", "Broken", null,
            new PortableTrigger("nonexistent", "nope"),
            [new PortableStep("Step", "nonexistent", "nope", "do", "{}", 1, 0)]);

        var dry = await (await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=true", doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.False(dry!.Valid);
        Assert.NotEmpty(dry.Issues);

        var real = await _client.PostAsJsonAsync($"/api/workspaces/{Ws}/flows/import?dryRun=false", doc);
        Assert.Equal(HttpStatusCode.BadRequest, real.StatusCode);
    }
}
