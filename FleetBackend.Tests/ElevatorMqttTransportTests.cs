using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using FleetBackend.Models;
using FleetBackend.Models.ElevatorMqtt;
using FleetBackend.Options;
using FleetBackend.Services.Elevator;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace FleetBackend.Tests;

public sealed class ElevatorMqttTransportTests
{
    [Fact]
    public async Task LocalBroker_SubscribePublishReceiveReconnectAndModeIsolation()
    {
        // Reserve a free loopback port without depending on a developer's broker.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var broker = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointPort(port).Build());
        await broker.StartAsync();
        using var controller = new MqttClientFactory().CreateMqttClient();
        var requests = Channel.CreateUnbounded<MqttApplicationMessage>();
        controller.ApplicationMessageReceivedAsync += args =>
        {
            requests.Writer.TryWrite(args.ApplicationMessage);
            return Task.CompletedTask;
        };
        await controller.ConnectAsync(new MqttClientOptionsBuilder().WithClientId("test-controller")
            .WithTcpServer("127.0.0.1", port).Build());
        await controller.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(filter => filter.WithTopic("elevator/request")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)).Build());

        var options = Microsoft.Extensions.Options.Options.Create(new ElevatorMqttOptions
        {
            Enabled = true, Host = "127.0.0.1", Port = port,
            ClientId = "test-fleet", ReconnectDelaySeconds = 1
        });
        var gateway = new CommunicationGateway(new JsonSerializerOptions()) { SystemMode = SystemMode.Operation };
        using var transport = new MqttElevatorTransport(options, gateway, NullLogger<MqttElevatorTransport>.Instance);
        using var adapter = new MqttElevatorAdapter(transport, options, NullLogger<MqttElevatorAdapter>.Instance);
        await transport.StartAsync(CancellationToken.None);
        try
        {
            var startup = await NextRequest();
            Assert.Equal("GET_STATUS", Command(startup));
            Assert.True(transport.IsReady);

            var call = adapter.CallAsync("Elevator_01", 2, TimeSpan.FromSeconds(5));
            var request = await NextRequest();
            Assert.Equal("CALL_ELEVATOR", Command(request));
            Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, request.QualityOfServiceLevel);
            Assert.False(request.Retain);
            using var requestDocument = JsonDocument.Parse(request.ConvertPayloadToString());
            Assert.Equal("robot3", requestDocument.RootElement.GetProperty("robot_id").GetString());
            Assert.False(call.IsCompleted); // Broker PUBACK alone must not finish the operation.
            await controller.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("elevator/status")
                .WithPayload("{\"current_floor\":2,\"state\":\"ARRIVED\"}")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build());
            Assert.Equal(2, (await call.WaitAsync(TimeSpan.FromSeconds(5))).CurrentFloor);

            var interrupted = adapter.CallAsync("Elevator_01", 1, TimeSpan.FromSeconds(10));
            Assert.Equal("CALL_ELEVATOR", Command(await NextRequest()));
            var connection = Assert.Single(await broker.GetClientsAsync(), client => client.Id == "test-fleet");
            await connection.DisconnectAsync();
            await Assert.ThrowsAsync<IOException>(() => interrupted.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("GET_STATUS", Command(await NextRequest()));
            Assert.False(requests.Reader.TryRead(out _)); // Reconnect does not replay the interrupted call.

            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.ConnectionChanged += connected => { if (!connected) disconnected.TrySetResult(); };
            gateway.SystemMode = SystemMode.Simulation;
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(adapter.GetSnapshot("Elevator_01").IsConnected);
            await Assert.ThrowsAsync<IOException>(() => transport.PublishAsync(ElevatorMqttCommand.OpenDoor(), CancellationToken.None));
        }
        finally
        {
            await transport.StopAsync(CancellationToken.None);
            await controller.DisconnectAsync();
            await broker.StopAsync();
        }

        async Task<MqttApplicationMessage> NextRequest() =>
            await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8));
        static string? Command(MqttApplicationMessage message)
        {
            using var document = JsonDocument.Parse(message.ConvertPayloadToString());
            return document.RootElement.GetProperty("command").GetString();
        }
    }
}
