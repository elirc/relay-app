using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Relay.Domain.Execution;
using Relay.Domain.Security;
using Relay.Domain.Time;
using Relay.Infrastructure.Execution;
using Relay.Infrastructure.Persistence;
using Relay.Infrastructure.Scheduling;
using Relay.Infrastructure.Security;
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
        services.AddSingleton<IDelayer, TaskDelayer>();
        services.AddScoped<IFlowExecutor, FlowExecutor>();

        // Scheduling: a clock port (fakeable in tests) + the tick dispatcher.
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ScheduleDispatcher>();

        // Secrets: envelope encryption over a local KMS. The master key is
        // derived (SHA-256) from configuration so any dev string works.
        var masterKey = SHA256.HashData(Encoding.UTF8.GetBytes(
            configuration["Secrets:MasterKey"] ?? "relay-dev-master-key"));
        services.AddSingleton<IKeyManagementService>(new LocalKeyManagementService(masterKey));
        services.AddSingleton<ISecretProtector, EnvelopeSecretProtector>();

        return services;
    }
}
