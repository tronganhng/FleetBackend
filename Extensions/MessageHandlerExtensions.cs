namespace FleetBackend.Extensions;

public static class MessageHandlerExtensions
{
    public static IServiceCollection AddMessageHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IMessageHandler, RegisterRobotHandler>();
        services.AddSingleton<IMessageHandler, CreateTaskHandler>();
        services.AddSingleton<IMessageHandler, CancelTaskHandler>();
        services.AddSingleton<IMessageHandler, SyncRobotStateHandler>();
        services.AddSingleton<IMessageHandler, ResourceAccessHandler>();
        services.AddSingleton<IMessageHandler, ResourceReleaseHandler>();
        services.AddSingleton<IMessageHandler, RobotArrivedHandler>();
        services.AddSingleton<IMessageHandler, SetSystemModeHandler>();
        return services;
    }
}