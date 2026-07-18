using Microsoft.EntityFrameworkCore;
using Relay.Domain.Enums;
using Relay.Infrastructure.Persistence;
using Relay.Tests.Support;

namespace Relay.Tests;

/// <summary>
/// Guards against schema drift: the EF model, the checked-in migrations, and the
/// seeder must all agree. A model change that isn't captured by a migration would
/// silently diverge production (which runs <see cref="DatabaseFacade.Migrate()"/>)
/// from the tests (which build the schema from the same migrations).
/// </summary>
public sealed class MigrationDriftTests
{
    [Fact]
    public void Model_HasNoPendingChanges_NotCapturedByAMigration()
    {
        using var database = new SqliteTestDatabase();
        using var ctx = database.CreateContext();

        // True when the current model differs from the last migration's snapshot —
        // i.e. someone changed an entity without running `dotnet ef migrations add`.
        Assert.False(
            ctx.Database.HasPendingModelChanges(),
            "The EF model has changes not captured by a migration. Run `dotnet ef migrations add` in Relay.Infrastructure.");
    }

    [Fact]
    public async Task Seeder_RunsCleanly_OnAFreshlyMigratedDatabase()
    {
        // Mirrors the production startup path: Migrate() then seed the catalog +
        // demo data. SqliteTestDatabase applies the real migrations in its ctor.
        using var database = new SqliteTestDatabase();
        await using var ctx = database.CreateContext();

        await DatabaseSeeder.SeedAsync(ctx);

        await using var read = database.CreateContext();
        Assert.Equal(5, await read.Connectors.CountAsync());
        Assert.Equal(5, await read.ConnectorVersions.CountAsync());
        Assert.True(await read.FlowTemplates.AnyAsync());
        Assert.Equal(1, await read.Workspaces.CountAsync());
        // The demo admin the getting-started walkthrough logs in as.
        Assert.True(await read.Users.AnyAsync(u => u.Email == "owner@acme.test" && u.Role == WorkspaceRole.Admin));
    }

    [Fact]
    public async Task AllMigrations_AreApplied_AndNonePending()
    {
        using var database = new SqliteTestDatabase();
        await using var ctx = database.CreateContext();

        // Every checked-in migration should have been applied by the ctor's Migrate().
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await ctx.Database.GetAppliedMigrationsAsync());
    }
}
