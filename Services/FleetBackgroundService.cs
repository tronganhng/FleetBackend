using FleetBackend.Services;

public class FleetBackgroundService : BackgroundService
{
    private readonly IScheduler _scheduler;
    private readonly IRobotBatteryWatcher _robotBatteryWatcher;

    public FleetBackgroundService(IScheduler robotMonitor, IRobotBatteryWatcher robotBatteryWatcher)
    {
        _scheduler = robotMonitor;
        _robotBatteryWatcher = robotBatteryWatcher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _scheduler.Tick();
            _robotBatteryWatcher.Tick();
            await Task.Delay(1000, stoppingToken);
        }
    }
}