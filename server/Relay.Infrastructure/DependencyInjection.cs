using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relay.Infrastructure.Persistence;

namespace Relay.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the EF Core SQLite <see cref="RelayDbContext"/>. Connection
    /// string comes from configuration key "ConnectionStrings:Relay", falling
    /// back to a local file database.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Relay")
            ?? "Data Source=relay.db";

        services.AddDbContext<RelayDbContext>(options =>
            options.UseSqlite(connectionString));

        return services;
    }
}
