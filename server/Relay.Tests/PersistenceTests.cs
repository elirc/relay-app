using Microsoft.EntityFrameworkCore;
using Relay.Domain.Entities;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Migrations_CreateSchema_AndSeederPopulatesCatalogAndDemoData()
    {
        using var database = new SqliteTestDatabase();
        await using var seedCtx = database.CreateContext();

        await DatabaseSeeder.SeedAsync(seedCtx);

        await using var readCtx = database.CreateContext();
        Assert.Equal(5, await readCtx.Connectors.CountAsync());

        var flow = await readCtx.Flows
            .Include(f => f.Steps)
            .SingleAsync(f => f.Id == DatabaseSeeder.DemoFlowId);
        Assert.Equal("Notify Slack on new signup", flow.Name);
        Assert.Single(flow.Steps);

        var webhook = await readCtx.Webhooks.SingleAsync();
        Assert.Equal("demo-signup-hook", webhook.Token);
    }

    [Fact]
    public async Task Seeder_IsIdempotent()
    {
        using var database = new SqliteTestDatabase();
        await using var ctx = database.CreateContext();

        await DatabaseSeeder.SeedAsync(ctx);
        await DatabaseSeeder.SeedAsync(ctx);

        await using var readCtx = database.CreateContext();
        Assert.Equal(5, await readCtx.Connectors.CountAsync());
        Assert.Equal(1, await readCtx.Workspaces.CountAsync());
    }

    [Fact]
    public async Task WorkspaceSlug_IsUnique()
    {
        using var database = new SqliteTestDatabase();
        await using var ctx = database.CreateContext();

        ctx.Workspaces.Add(new Workspace { Name = "A", Slug = "dup", CreatedAtUtc = DateTimeOffset.UtcNow });
        ctx.Workspaces.Add(new Workspace { Name = "B", Slug = "dup", CreatedAtUtc = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Enums_PersistAsStrings()
    {
        using var database = new SqliteTestDatabase();
        await using var ctx = database.CreateContext();
        await DatabaseSeeder.SeedAsync(ctx);

        // Read the raw column value to prove the enum is stored as text, not an int.
        var raw = await ctx.Database
            .SqlQuery<string>($"SELECT AuthKind AS Value FROM Connectors WHERE \"Key\" = 'slack'")
            .SingleAsync();

        Assert.Equal(nameof(AuthKind.OAuth2), raw);
    }
}
