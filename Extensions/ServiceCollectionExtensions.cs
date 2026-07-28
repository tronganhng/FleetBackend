using FleetBackend.Services;

namespace FleetBackend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFleetCore(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, EventBus>();
        // Managers
        services.AddHostedService<FleetBackgroundService>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IMapManager, MapManager>();
        services.AddSingleton<IRobotManager, RobotManager>();
        services.AddSingleton<ITaskManager, TaskManager>();
        services.AddSingleton<ITrafficManager, TrafficManager>();
        services.AddSingleton<IScheduler, Scheduler>();
        return services;
    }
}