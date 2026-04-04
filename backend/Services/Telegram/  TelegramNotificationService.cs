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
        await SendMessageAsync(text, cancellationToken);
    }

    public async Task SendIncidentUpdatedAsync(Incident incident, CancellationToken cancellationToken = default)
    {
        var text = _formatter.FormatIncidentUpdated(incident);
        await SendMessageAsync(text, cancellationToken);
    }

    private async Task SendMessageAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.ChatId))
        {
            _logger.LogWarning("Telegram settings are missing. Notification skipped.");
            return;
        }

        var url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";

        var payload = new
        {
            chat_id = _options.ChatId,
            text
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Telegram send failed. Status={StatusCode}, Body={Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram notification failed.");
        }
    }
}