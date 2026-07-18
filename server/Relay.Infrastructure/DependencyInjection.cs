using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relay.Domain.Execution;
using Relay.Domain.Time;
using Relay.Infrastructure.Execution;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Scheduling;
using Relay.Infrastructure.Time;

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

        // Flow execution over the IActionDispatcher port (no real external calls).
        services.AddSingleton<IActionDispatcher, SimulatedActionDispatcher>();
        services.AddScoped<IFlowExecutor, FlowExecutor>();

        // Scheduling: a clock port (fakeable in tests) + the tick dispatcher.
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ScheduleDispatcher>();

        return services;
    }
}
