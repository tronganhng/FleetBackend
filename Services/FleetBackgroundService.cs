using FleetBackend.Services;

public class FleetBackgroundService : BackgroundService
{
    private readonly IScheduler _scheduler;

    public FleetBackgroundService(IScheduler robotMonitor)
    {
        _scheduler = robotMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _scheduler.Tick();
            await Task.Delay(1000, stoppingToken);
        }
    }
}