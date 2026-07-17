using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Relay.Infrastructure.Persistence;

namespace Relay.Tests.Support;

/// <summary>
/// An isolated in-memory SQLite database whose schema is created by applying the
/// real EF Core migrations. The connection is held open for the lifetime of the
/// instance so the in-memory database survives between contexts. Create fresh
/// contexts with <see cref="CreateContext"/> to defeat the identity map when
/// asserting round-trips.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RelayDbContext> _options;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<RelayDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = CreateContext();
        ctx.Database.Migrate();
    }

    public RelayDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
