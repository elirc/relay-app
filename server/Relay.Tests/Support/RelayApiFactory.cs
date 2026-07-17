using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Relay.Api.Contracts.Auth;
using Relay.Infrastructure.Persistence;

namespace Relay.Tests.Support;

/// <summary>
/// Boots the real API in the "Testing" environment (so the production startup
/// migrate/seed is skipped) and swaps the SQLite file database for a private
/// in-memory one kept alive by an open connection. Reused across API tests.
/// </summary>
public class RelayApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public RelayApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<RelayDbContext>>();
            services.RemoveAll<RelayDbContext>();
            services.AddDbContext<RelayDbContext>(options => options.UseSqlite(_connection));
        });
    }

    /// <summary>Applies migrations and seeds the catalog + demo data once.</summary>
    public async Task SeedAsync(bool includeDemoData = true)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(db, includeDemoData);
    }

    /// <summary>Runs an action against a scoped <see cref="RelayDbContext"/>.</summary>
    public async Task WithDbAsync(Func<RelayDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
        await action(db);
    }

    /// <summary>Logs in and returns a bearer token for the given credentials.</summary>
    public async Task<string> LoginAsync(string email = "owner@acme.test", string password = "password123")
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(TestJson.Options);
        return body!.Token;
    }

    /// <summary>Attaches a bearer token to <paramref name="client"/> (default: the seeded Admin owner).</summary>
    public async Task AuthenticateAsync(
        HttpClient client,
        string email = "owner@acme.test",
        string password = "password123")
    {
        var token = await LoginAsync(email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _connection.Dispose();
        base.Dispose(disposing);
    }
}
