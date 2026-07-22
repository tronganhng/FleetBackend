namespace FleetBackend.Extensions;

public static class AlgorithmsExtensions
{
    public static IServiceCollection AddAlgorithms(this IServiceCollection services)
    {
        services.AddSingleton<ICostCaculator, CostCaculator>();
        services.AddSingleton<IPathFinder, AStarPathFinder>();
        return services;
    }
}