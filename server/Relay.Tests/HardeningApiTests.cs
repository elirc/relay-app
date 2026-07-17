using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Relay.Api.Contracts.Common;
using Relay.Api.Contracts.Connectors;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class HardeningApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;

    public HardeningApiTests(RelayApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.SeedAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Pagination_ReturnsRequestedPage_WithMetadata()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ConnectorDto>>(
            "/api/connectors?page=1&pageSize=2", TestJson.Options);

        Assert.Equal(2, page!.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(5, page.TotalCount); // seeded catalog
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task Pagination_ClampsPageSizeToMax()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ConnectorDto>>(
            "/api/connectors?pageSize=5000", TestJson.Options);

        Assert.Equal(PaginationQuery.MaxPageSize, page!.PageSize);
    }

    [Fact]
    public async Task Pagination_PageBeyondRange_ReturnsEmptyItemsButRealTotal()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<ConnectorDto>>(
            "/api/connectors?page=99&pageSize=2", TestJson.Options);

        Assert.Empty(page!.Items);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task Validation_InvalidConnectorKey_Returns400_ProblemJson()
    {
        var response = await _client.PostAsJsonAsync("/api/connectors",
            new CreateConnectorRequest("Bad Key!", "Name", "d", AuthKind.None, "{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Validation_MalformedJson_Returns400()
    {
        var content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/connectors", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProblemDetails_NotFound_IncludesTraceIdAndInstance()
    {
        var response = await _client.GetAsync($"/api/connectors/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("traceId", out _), "traceId extension missing");
        Assert.True(root.TryGetProperty("instance", out var instance));
        Assert.StartsWith("/api/connectors/", instance.GetString());
        Assert.Equal(404, root.GetProperty("status").GetInt32());
    }
}
