using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Models.ElevatorMqtt;
using FleetBackend.Options;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace FleetBackend.Services.Elevator;

public sealed class MqttElevatorTransport : BackgroundService, IElevatorMqttTransport
{
    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private readonly ElevatorMqttOptions _options;
    private readonly ICommunicationGateway _gateway;
    private readonly ILogger<MqttElevatorTransport> _logger;
    private volatile bool _ready;
    public bool IsReady => _ready;
    public event Action<bool>? ConnectionChanged;
    public event Action<ElevatorMqttMessage>? MessageReceived;

    public MqttElevatorTransport(IOptions<ElevatorMqttOptions> options,
        ICommunicationGateway gateway, ILogger<MqttElevatorTransport> logger)
    {
        _options = options.Value;
        _gateway = gateway;
        _logger = logger;
        _client.ApplicationMessageReceivedAsync += args =>
        {
            var message = args.ApplicationMessage;
            var payload = message.ConvertPayloadToString();
            _logger.LogDebug("Elevator RX {Topic} retained={Retained} at {ReceivedAtUtc}: {Payload}",
                message.Topic, message.Retain, DateTimeOffset.UtcNow, payload);
            // No publish/wait inside MQTT receive callbacks.
            Notify(MessageReceived, new(message.Topic, payload, message.Retain));
            return global::System.Threading.Tasks.Task.CompletedTask;
        };
        _client.DisconnectedAsync += _ =>
        {
            SetReady(false);
            return global::System.Threading.Tasks.Task.CompletedTask;
        };
    }

    protected override async global::System.Threading.Tasks.Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(_options.ClientId)
            .WithTcpServer(_options.Host, _options.Port)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
            .Build();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_options.Enabled || _gateway.SystemMode != SystemMode.Operation)
                {
                    await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }
                try
                {
                    using var connection = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    connection.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));
                    await _client.ConnectAsync(clientOptions, connection.Token);
                    var subscription = await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(filter => filter.WithTopic(_options.StatusTopic)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)).Build(), connection.Token);
                    if (subscription.Items.Any(item => (int)item.ResultCode >= 128))
                        throw new IOException("Broker rejected the elevator status subscription.");

                    // SUBACK first, then request current state on every connection. No replay of physical commands.
                    SetReady(true);
                    await PublishAsync(ElevatorMqttCommand.GetStatus(), connection.Token);
                    while (_client.IsConnected && _gateway.SystemMode == SystemMode.Operation)
                        await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Elevator MQTT connection unavailable; retrying connection only");
                }
                finally { await DisconnectAsync(); }

                await global::System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await DisconnectAsync(); }
    }

    public async global::System.Threading.Tasks.Task PublishAsync(ElevatorMqttCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_ready || !_client.IsConnected || !_options.Enabled || _gateway.SystemMode != SystemMode.Operation)
            throw new IOException("Elevator MQTT is not ready in Operation mode.");
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(_options.RequestTopic)
            .WithPayload(JsonSerializer.Serialize(command))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(false)
            .Build();
        var result = await _client.PublishAsync(message, cancellationToken);
        if ((int)result.ReasonCode >= 128)
            throw new IOException($"Broker rejected elevator command: {result.ReasonCode}.");
    }

    private void SetReady(bool ready)
    {
        if (_ready == ready) return;
        _ready = ready;
        Notify(ConnectionChanged, ready);
    }

    private void Notify<T>(Action<T>? subscribers, T value)
    {
        if (subscribers == null) return;
        foreach (Action<T> subscriber in subscribers.GetInvocationList())
        {
            try { subscriber(value); }
            catch (Exception ex) { _logger.LogError(ex, "Elevator MQTT subscriber failed"); }
        }
    }

    private async global::System.Threading.Tasks.Task DisconnectAsync()
    {
        SetReady(false);
        if (!_client.IsConnected) return;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), timeout.Token);
        }
        catch (Exception ex) { _logger.LogDebug(ex, "Elevator MQTT disconnect failed"); }
    }

    public override void Dispose()
    {
        base.Dispose();
        SetReady(false);
        _client.Dispose();
    }
}
