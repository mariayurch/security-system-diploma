using System.Text;
using backend.Models;
using backend.Models.Enums;

namespace backend.Services.Telegram;

public class TelegramMessageFormatter
{
    public string FormatIncidentCreated(Incident incident)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"{GetIncidentEmoji(incident)} Новий інцидент");
        sb.AppendLine($"Тип: {incident.IncidentType}");
        sb.AppendLine($"Рівень: {incident.Confidence}");
        sb.AppendLine($"Зона: {incident.Zone}");
        sb.AppendLine($"Час: {incident.StartedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(incident.Explanation))
            sb.AppendLine($"Пояснення: {incident.Explanation}");

        return sb.ToString();
    }

    public string FormatIncidentUpdated(Incident incident)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"{GetIncidentEmoji(incident)} Оновлення інциденту");
        sb.AppendLine($"ID: {incident.Id}");
        sb.AppendLine($"Тип: {incident.IncidentType}");
        sb.AppendLine($"Рівень: {incident.Confidence}");
        sb.AppendLine($"Зона: {incident.Zone}");

        if (!string.IsNullOrWhiteSpace(incident.Explanation))
        {
            sb.AppendLine();
            sb.AppendLine($"Пояснення: {incident.Explanation}");
        }

        return sb.ToString();
    }

    private static string GetIncidentEmoji(Incident incident) =>
        incident.IncidentType switch
        {
            IncidentType.Intrusion => incident.Confidence == IncidentConfidence.Confirmed ? "🚨" : "⚠️",
            IncidentType.Sabotage => "🛠️",
            IncidentType.SensorAnomaly => "⚙️",
            IncidentType.Panic => "🆘",
            _ => "📢"
        };

}