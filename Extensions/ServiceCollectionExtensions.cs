using FleetBackend.Services;
using Microsoft.Extensions.DependencyInjection;

namespace FleetBackend.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFleetCore(this IServiceCollection services)
    {
        // Managers
        services.AddSingleton<IRobotManager, RobotManager>();
        services.AddSingleton<ITaskManager, TaskManager>();
        return services;
    }
}