namespace MediQueue.Services;

/// <summary>Every 20 seconds: marks patients who did not come after being called, and closes old tokens.</summary>
public sealed class HousekeepingService : BackgroundService
{
    private readonly QueueService _queue;
    private readonly ILogger<HousekeepingService> _log;

    public HousekeepingService(QueueService queue, ILogger<HousekeepingService> log)
    {
        _queue = queue;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        try
        {
            do
            {
                try
                {
                    var n = _queue.RunHousekeeping();
                    if (n > 0) _log.LogInformation("Housekeeping updated {Count} token(s).", n);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Housekeeping failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // server is stopping
        }
    }
}
