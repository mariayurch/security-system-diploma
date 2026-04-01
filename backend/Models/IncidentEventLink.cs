namespace backend.Models;

public class IncidentEventLink
{
    public int IncidentId { get; set; }
    public Incident Incident { get; set; } = default!;

    public int SecurityEventId { get; set; }
    public SecurityEvent SecurityEvent { get; set; } = default!;
}