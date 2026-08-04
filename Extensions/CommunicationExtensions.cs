namespace FleetBackend.Extensions;

public static class CommunicationExtensions
{
    public static IServiceCollection AddCommunication(this IServiceCollection services)
    {
        services.AddSingleton<ICommunicationGateway, CommunicationGateway>();
        services.AddSingleton<IServerReciever, ServerReciever>();
        services.AddSingleton<MessageRouter>();
        return services;
    }
}