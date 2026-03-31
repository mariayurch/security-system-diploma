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

    // технічний час від ESP32 (millis / uptime)
    public long Ts { get; set; }

    // людський час, коли backend реально отримав подію
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}