using System.Text;
using System.Text.Json;
using backend.Dtos;
using MQTTnet;

namespace backend.Services;

public class MqttListenerService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MqttListenerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private IMqttClient? _client;

    public MqttListenerService(
        IConfiguration configuration,
        ILogger<MqttListenerService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
                var sourceTopic = e.ApplicationMessage.Topic;

                _logger.LogInformation("MQTT message received from topic: {Topic}", sourceTopic);
                _logger.LogInformation("MQTT raw payload: {Payload}", payload);

                var dto = JsonSerializer.Deserialize<EspEventDto>(payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (dto is null)
                {
                    _logger.LogWarning("Failed to deserialize payload");
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var ingestionService = scope.ServiceProvider.GetRequiredService<EventIngestionService>();

                await ingestionService.SaveEventAsync(dto, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing MQTT message");
            }
        };

        var host = _configuration["Mqtt:Host"]!;
        var port = int.Parse(_configuration["Mqtt:Port"]!);
        var topics = _configuration.GetSection("Mqtt:Topics").Get<string[]>() ?? Array.Empty<string>();
        var clientId = _configuration["Mqtt:ClientId"]!;

        var options = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithTcpServer(host, port)
            .Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_client is not null && !_client.IsConnected)
                {
                    _logger.LogInformation("Connecting to MQTT broker...");
                    await _client.ConnectAsync(options, stoppingToken);
                    _logger.LogInformation("Connected to MQTT broker");

                    foreach (var topic in topics)
                    {
                        await _client.SubscribeAsync(topic, cancellationToken: stoppingToken);
                        _logger.LogInformation("Subscribed to topic: {Topic}", topic);
                    }
                }

                await Task.Delay(3000, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT connection failed");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}