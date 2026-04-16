using System.Net.Http.Json;
using backend.Models;
using Microsoft.Extensions.Options;

namespace backend.Services.Telegram;

public class TelegramNotificationService : ITelegramNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;
    private readonly TelegramMessageFormatter _formatter;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(
        HttpClient httpClient,
        IOptions<TelegramOptions> options,
        TelegramMessageFormatter formatter,
        ILogger<TelegramNotificationService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _formatter = formatter;
        _logger = logger;
    }

    public async Task SendIncidentCreatedAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        var text = _formatter.FormatIncidentCreated(incident);
        await SendAsync(text, "incident_created", cancellationToken);
    }

    public async Task SendIncidentUpdatedAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        var text = _formatter.FormatIncidentUpdated(incident);
        await SendAsync(text, "incident_updated", cancellationToken);
    }

    private async Task SendAsync(string text, string messageType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.ChatId))
        {
            _logger.LogWarning("Telegram BotToken or ChatId is not configured. Skipping notification.");
            return;
        }

        var url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";

        var payload = new
        {
            chat_id = _options.ChatId,
            text,
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Telegram notification sent successfully. Type={MessageType}, ChatId={ChatId}",
                    messageType,
                    _options.ChatId);

                return;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogError(
                "Telegram send failed. Type={MessageType}, StatusCode={StatusCode}, ChatId={ChatId}, ResponseBody={ResponseBody}",
                messageType,
                (int)response.StatusCode,
                _options.ChatId,
                responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error while sending Telegram notification. Type={MessageType}, ChatId={ChatId}",
                messageType,
                _options.ChatId);
        }
    }
}