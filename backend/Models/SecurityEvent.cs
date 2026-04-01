namespace backend.Models;

public class SecurityEvent
{
    public int Id { get; set; }

    public long EventId { get; set; }
    public string DeviceId { get; set; } = default!;
    public string Zone { get; set; } = default!;
    public string Sensor { get; set; } = default!;
    public string Event { get; set; } = default!;
    public bool Armed { get; set; }
    public int Rssi { get; set; }
    public long Ts { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<IncidentEventLink> IncidentEventLinks { get; set; } = new List<IncidentEventLink>();
}