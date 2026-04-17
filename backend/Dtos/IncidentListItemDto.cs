namespace backend.Dtos;

public class IncidentListItemDto
{
    public int Id { get; set; }
    public string IncidentType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string? Zone { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? Explanation { get; set; }
}