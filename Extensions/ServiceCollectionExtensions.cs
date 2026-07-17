using FleetBackend.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FleetBackend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFleetCore(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, EventBus>();
        // Managers
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IRobotManager, RobotManager>();
        services.AddSingleton<ITaskManager, TaskManager>();
        services.AddSingleton<IScheduler, Scheduler>();
        return services;
    }
}