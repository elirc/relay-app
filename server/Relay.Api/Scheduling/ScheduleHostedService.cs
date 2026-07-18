using Relay.Infrastructure.Scheduling;

namespace Relay.Api.Scheduling;

/// <summary>
/// Ticks the <see cref="ScheduleDispatcher"/> on a fixed interval in a fresh DI
/// scope. Registered only outside the test host, where scheduling is driven
/// deterministically via a fake clock instead.
/// </summary>
public sealed class ScheduleHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduleHostedService> _logger;

    public ScheduleHostedService(IServiceScopeFactory scopeFactory, ILogger<ScheduleHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<ScheduleDispatcher>();
                var count = await dispatcher.RunDueSchedulesAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Scheduler triggered {Count} run(s).", count);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler tick failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
