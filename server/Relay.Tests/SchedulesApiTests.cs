using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Relay.Api.Contracts.Schedules;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class SchedulesApiTests : IClassFixture<RelayApiFactory>, IAsyncLifetime
{
    private readonly RelayApiFactory _factory;
    private readonly HttpClient _client;
    private static readonly Guid Ws = DatabaseSeeder.DemoWorkspaceId;
    private static readonly Guid Flow = DatabaseSeeder.DemoFlowId;
    private static string Base => $"/api/workspaces/{Ws}/flows/{Flow}/schedules";

    public SchedulesApiTests(RelayApiFactory factory)
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

    private async Task<ScheduleDto> CreateSchedule(string cron = "*/15 * * * *")
    {
        var response = await _client.PostAsJsonAsync(Base, new ScheduleRequest(cron));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options))!;
    }

    [Fact]
    public async Task Create_ValidCron_ComputesNextRun()
    {
        var response = await _client.PostAsJsonAsync(Base, new ScheduleRequest("0 9 * * *"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options);
        Assert.True(dto!.IsEnabled);
        Assert.NotNull(dto.NextRunAtUtc);
    }

    [Fact]
    public async Task Create_InvalidCron_Returns400()
    {
        var response = await _client.PostAsJsonAsync(Base, new ScheduleRequest("not a cron"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownFlow_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/workspaces/{Ws}/flows/{Guid.NewGuid()}/schedules", new ScheduleRequest("* * * * *"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsCreatedSchedules()
    {
        await CreateSchedule("*/30 * * * *");

        var list = await _client.GetFromJsonAsync<List<ScheduleDto>>(Base, TestJson.Options);
        Assert.NotNull(list);
        Assert.Contains(list!, s => s.CronExpression == "*/30 * * * *");
    }

    [Fact]
    public async Task Preview_ValidCron_ReturnsNextRuns()
    {
        var preview = await _client.GetFromJsonAsync<SchedulePreviewResponse>(
            $"{Base}/preview?cron={Uri.EscapeDataString("0 * * * *")}&count=3", TestJson.Options);

        Assert.True(preview!.Valid);
        Assert.Equal(3, preview.NextRuns.Count);
        Assert.True(preview.NextRuns[0] < preview.NextRuns[1]);
    }

    [Fact]
    public async Task Preview_InvalidCron_ReturnsInvalid()
    {
        var preview = await _client.GetFromJsonAsync<SchedulePreviewResponse>(
            $"{Base}/preview?cron={Uri.EscapeDataString("nope")}", TestJson.Options);

        Assert.False(preview!.Valid);
        Assert.Empty(preview.NextRuns);
    }

    [Fact]
    public async Task Disable_ClearsNextRun_EnableRearms()
    {
        var created = await CreateSchedule();

        var disabled = await _client.PostAsync($"{Base}/{created.Id}/disable", null);
        var disabledDto = await disabled.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options);
        Assert.False(disabledDto!.IsEnabled);
        Assert.Null(disabledDto.NextRunAtUtc);

        var enabled = await _client.PostAsync($"{Base}/{created.Id}/enable", null);
        var enabledDto = await enabled.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options);
        Assert.True(enabledDto!.IsEnabled);
        Assert.NotNull(enabledDto.NextRunAtUtc);
    }

    [Fact]
    public async Task Update_ChangesCron_AndRecomputesNextRun()
    {
        var created = await CreateSchedule("0 0 * * *");

        var response = await _client.PutAsJsonAsync($"{Base}/{created.Id}", new ScheduleRequest("0 12 * * *"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options);
        Assert.Equal("0 12 * * *", dto!.CronExpression);
        Assert.NotNull(dto.NextRunAtUtc);
    }

    [Fact]
    public async Task Delete_RemovesSchedule()
    {
        var created = await CreateSchedule();

        var deleted = await _client.DeleteAsync($"{Base}/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var list = await _client.GetFromJsonAsync<List<ScheduleDto>>(Base, TestJson.Options);
        Assert.DoesNotContain(list!, s => s.Id == created.Id);
    }

    [Fact]
    public async Task Member_CannotCreateSchedule_Returns403()
    {
        var memberClient = _factory.CreateClient();
        await _factory.WithDbAsync(async db =>
        {
            if (await db.Users.AnyAsync(u => u.Email == "sched-member@acme.test")) return;
            db.Users.Add(new Relay.Domain.Entities.User
            {
                Id = Guid.NewGuid(),
                WorkspaceId = Ws,
                Email = "sched-member@acme.test",
                DisplayName = "Sched Member",
                PasswordHash = Relay.Infrastructure.Security.PasswordHasher.Hash("password123"),
                Role = Relay.Domain.Enums.WorkspaceRole.Member,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        });
        await _factory.AuthenticateAsync(memberClient, "sched-member@acme.test", "password123");

        var response = await memberClient.PostAsJsonAsync(Base, new ScheduleRequest("* * * * *"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        memberClient.Dispose();
    }
}
