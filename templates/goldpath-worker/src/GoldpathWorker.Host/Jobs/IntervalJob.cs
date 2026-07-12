namespace GoldpathWorker.Host.Jobs;

/// <summary>
/// BCL <see cref="PeriodicTimer"/> skeleton (template-completion RFC D3): dependency-free
/// scheduling that the Ring C Jobs module later replaces without touching the host shape.
/// Put the actual work in <see cref="RunTickAsync"/> — the time-abstracted, directly
/// testable unit; keep the timer loop free of business logic.
/// </summary>
public sealed class IntervalJob(ILogger<IntervalJob> logger, IConfiguration configuration) : BackgroundService
{
    /// <summary>Ticks executed so far (smoke-observable via /api/v1/ticks).</summary>
    public int TickCount { get; private set; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue("Worker:Interval", TimeSpan.FromMinutes(1));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunTickAsync();
        }
    }

    /// <summary>One unit of scheduled work — replace the log line with the real job.</summary>
    public Task RunTickAsync()
    {
        TickCount++;
        logger.LogInformation("Interval tick {TickCount} executed.", TickCount);
        return Task.CompletedTask;
    }
}
