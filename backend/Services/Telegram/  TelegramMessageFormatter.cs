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
        sb.AppendLine($"ID: {incident.Id}");
        sb.AppendLine($"Тип: {incident.IncidentType}");
        sb.AppendLine($"Рівень: {GetConfidenceText(incident.Confidence)}");
        sb.AppendLine($"Статус: {GetStatusText(incident.Status)}");
        sb.AppendLine($"Зона: {incident.Zone}");
        sb.AppendLine($"Час: {incident.StartedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");

        if (!string.IsNullOrWhiteSpace(incident.Explanation))
        {
            sb.AppendLine();
            sb.AppendLine($"Пояснення: {incident.Explanation}");
        }

        return sb.ToString();
    }

    public string FormatIncidentUpdated(Incident incident)
    {
        var sb = new StringBuilder();

        string title = incident.Status switch
        {
            IncidentStatus.Acknowledged => "Інцидент взято в роботу",
            IncidentStatus.Closed => "Інцидент закрито",
            _ when incident.Confidence == IncidentConfidence.Confirmed => "Підтверджений інцидент",
            _ => "Оновлення інциденту"
        };

        sb.AppendLine($"{GetIncidentEmoji(incident)} {title}");
        sb.AppendLine($"ID: {incident.Id}");
        sb.AppendLine($"Тип: {incident.IncidentType}");
        sb.AppendLine($"Рівень: {GetConfidenceText(incident.Confidence)}");
        sb.AppendLine($"Статус: {GetStatusText(incident.Status)}");
        sb.AppendLine($"Зона: {incident.Zone}");
        sb.AppendLine($"Час: {incident.StartedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");

        if (incident.ClosedAtUtc is not null)
        {
            sb.AppendLine($"Закрито: {incident.ClosedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        }

        if (!string.IsNullOrWhiteSpace(incident.Explanation))
        {
            sb.AppendLine();
            sb.AppendLine($"Пояснення: {incident.Explanation}");
        }

        return sb.ToString();
    }

    private static string GetIncidentEmoji(Incident incident)
    {
        if (incident.Status == IncidentStatus.Closed)
            return "✅";

        if (incident.Status == IncidentStatus.Acknowledged)
            return "📝";

        return incident.IncidentType switch
        {
            IncidentType.Intrusion => incident.Confidence == IncidentConfidence.Confirmed ? "🚨" : "⚠️",
            IncidentType.Sabotage => "🛠️",
            IncidentType.Panic => "🆘",
            IncidentType.SensorAnomaly => "⚙️",
            _ => "🔔"
        };
    }

private static string GetStatusText(IncidentStatus status)
{
    return status switch
    {
        IncidentStatus.Open => "Відкритий",
        IncidentStatus.Acknowledged => "Взятий в роботу",
        IncidentStatus.Closed => "Закритий",
        _ => status.ToString()
    };
}

private static string GetConfidenceText(IncidentConfidence confidence)
{
    return confidence switch
    {
        IncidentConfidence.Suspected => "Підозрілий",
        IncidentConfidence.Confirmed => "Підтверджений",
        _ => confidence.ToString()
    };
}

}