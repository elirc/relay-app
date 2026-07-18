using System.Net;
using System.Net.Http.Json;
using Relay.Api.Controllers;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class HealthEndpointTests : IClassFixture<RelayApiFactory>
{
    private readonly RelayApiFactory _factory;

    public HealthEndpointTests(RelayApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_Health_ReturnsOkStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_ReturnsExpectedPayload()
    {
        var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<HealthResponse>("/health");

        Assert.NotNull(body);
        Assert.Equal("ok", body!.Status);
        Assert.Equal("relay-api", body.Service);
    }

    [Fact]
    public async Task Get_Health_IncludesDatabaseProbe_AndVersion()
    {
        var client = _factory.CreateClient();

        var body = await client.GetFromJsonAsync<HealthResponse>("/health");

        Assert.Equal("ok", body!.Checks.Database);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
    }
}
