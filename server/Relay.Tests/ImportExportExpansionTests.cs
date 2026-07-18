using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Flows;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Portability guarantees beyond the happy path: dry-run has zero side effects,
/// export→import round-trips to an equal document, and re-import by external id
/// never duplicates steps or flows.
/// </summary>
public sealed class ImportExportExpansionTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;

    public ImportExportExpansionTests(RelayApiFactory factory)
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

    private Task<FlowExportDto?> ExportDemo() =>
        _client.GetFromJsonAsync<FlowExportDto>(
            $"/api/workspaces/{Ws}/flows/{DatabaseSeeder.DemoFlowId}/export", TestJson.Options);

    private async Task<int> FlowCount()
    {
        var count = 0;
        await _factory.WithDbAsync(async db => count = await db.Flows.CountAsync(f => f.WorkspaceId == Ws));
        return count;
    }

    [Fact]
    public async Task DryRun_HasZeroSideEffects()
    {
        var export = (await ExportDemo())!;
        var doc = export with { ExternalId = "dry-zero-1", Name = "Should not persist" };

        var before = await FlowCount();
        var result = await (await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=true", doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);

        Assert.True(result!.Valid);
        Assert.Null(result.FlowId);
        Assert.Equal(before, await FlowCount()); // nothing created

        // And no flow carries that external id.
        var persisted = false;
        await _factory.WithDbAsync(async db =>
            persisted = await db.Flows.AnyAsync(f => f.WorkspaceId == Ws && f.ExternalId == "dry-zero-1"));
        Assert.False(persisted);
    }

    [Fact]
    public async Task ExportImportRoundTrip_ProducesAnEqualDocument()
    {
        var original = (await ExportDemo())!;
        var doc = original with { ExternalId = "round-trip-1", Name = "Round trip" };

        var import = await (await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=false", doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.True(import!.Valid);

        var reExport = await _client.GetFromJsonAsync<FlowExportDto>(
            $"/api/workspaces/{Ws}/flows/{import.FlowId}/export", TestJson.Options);

        Assert.Equal("round-trip-1", reExport!.ExternalId);
        Assert.Equal(doc.Name, reExport.Name);
        Assert.Equal(doc.Trigger.ConnectorKey, reExport.Trigger.ConnectorKey);
        Assert.Equal(doc.Trigger.ConnectionName, reExport.Trigger.ConnectionName);
        Assert.Equal(doc.Steps.Count, reExport.Steps.Count);
        for (var i = 0; i < doc.Steps.Count; i++)
        {
            Assert.Equal(doc.Steps[i].ConnectorKey, reExport.Steps[i].ConnectorKey);
            Assert.Equal(doc.Steps[i].Action, reExport.Steps[i].Action);
            Assert.Equal(doc.Steps[i].Name, reExport.Steps[i].Name);
            Assert.Equal(doc.Steps[i].ConfigJson, reExport.Steps[i].ConfigJson);
        }
    }

    [Fact]
    public async Task ReImport_ByExternalId_DoesNotDuplicateFlowsOrSteps()
    {
        var export = (await ExportDemo())!;
        var doc = export with { ExternalId = "no-dupes-1", Name = "Idempotent" };
        var url = $"/api/workspaces/{Ws}/flows/import?dryRun=false";

        var first = await (await _client.PostAsJsonAsync(url, doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        var second = await (await _client.PostAsJsonAsync(url, doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        var third = await (await _client.PostAsJsonAsync(url, doc)).Content
            .ReadFromJsonAsync<ImportResultDto>(TestJson.Options);

        Assert.Equal("create", first!.Action);
        Assert.Equal("update", second!.Action);
        Assert.Equal("update", third!.Action);
        Assert.Equal(first.FlowId, third.FlowId);

        // Exactly one flow with this external id, and its step list wasn't multiplied.
        await _factory.WithDbAsync(async db =>
        {
            var flows = await db.Flows.Where(f => f.WorkspaceId == Ws && f.ExternalId == "no-dupes-1").ToListAsync();
            Assert.Single(flows);
            var stepCount = await db.FlowSteps.CountAsync(s => s.FlowId == flows[0].Id);
            Assert.Equal(doc.Steps.Count, stepCount);
        });
    }

    [Fact]
    public async Task Import_MissingName_Returns400_WithIssue()
    {
        var export = (await ExportDemo())!;
        var doc = export with { ExternalId = "bad-name-1", Name = "" };

        var response = await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/import?dryRun=false", doc);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ImportResultDto>(TestJson.Options);
        Assert.False(result!.Valid);
        Assert.Contains(result.Issues, i => i.Contains("name", StringComparison.OrdinalIgnoreCase));
    }
}
