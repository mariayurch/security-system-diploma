using backend.Models;

namespace backend.Services.Telegram;

public interface ITelegramNotificationService
{
    Task SendIncidentCreatedAsync(Incident incident, CancellationToken cancellationToken = default);
    Task SendIncidentUpdatedAsync(Incident incident, CancellationToken cancellationToken = default);
}