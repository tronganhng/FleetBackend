using FleetBackend.Services.Elevator;

namespace FleetBackend.Extensions;

public static class CommunicationExtensions
{
    public static IServiceCollection AddCommunication(this IServiceCollection services)
    {
        services.AddSingleton<ICommunicationGateway, CommunicationGateway>();
        services.AddSingleton<MqttElevatorTransport>();
        services.AddSingleton<IElevatorMqttTransport>(provider => provider.GetRequiredService<MqttElevatorTransport>());
        services.AddSingleton<MqttElevatorAdapter>();
        services.AddSingleton<SimulationElevatorAdapter>();
        services.AddSingleton<ElevatorAdapterResolver>();
        services.AddSingleton<ElevatorRecoveryStore>();
        services.AddSingleton<ElevatorRecoveryManager>();
        services.AddHostedService(provider =>
        {
            // Subscribe the adapter before the transport starts receiving status.
            provider.GetRequiredService<MqttElevatorAdapter>();
            return provider.GetRequiredService<MqttElevatorTransport>();
        });
        services.AddSingleton<IServerReceiver, ServerReceiver>();
        services.AddSingleton<MessageRouter>();
        return services;
    }
}
