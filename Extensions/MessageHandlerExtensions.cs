namespace FleetBackend.Extensions;

public static class MessageHandlerExtensions
{
    public static IServiceCollection AddMessageHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IMessageHandler, RegisterRobotHandler>();
        services.AddSingleton<IMessageHandler, CreateTaskHandler>();
        services.AddSingleton<IMessageHandler, CancelTaskHandler>();
        services.AddSingleton<IMessageHandler, UpdateTaskHandler>();
        services.AddSingleton<IMessageHandler, SyncRobotStateHandler>();
        return services;
    }
}