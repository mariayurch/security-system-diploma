using backend.Models.Enums;

namespace backend.Models;

public class Incident
{
    public int Id { get; set; }

    public IncidentType IncidentType { get; set; }
    public IncidentStatus Status { get; set; }
    public IncidentConfidence Confidence { get; set; }
    public string Zone { get; set; } = default!;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public string Explanation { get; set; } = default!;

    public ICollection<IncidentEventLink> IncidentEventLinks { get; set; } = new List<IncidentEventLink>();
}