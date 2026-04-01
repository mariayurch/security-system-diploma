using backend.Data;
using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class IncidentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<IncidentService> _logger;

    public IncidentService(AppDbContext db, ILogger<IncidentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessEventAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default)
    {
        if (securityEvent.Sensor == "panic_button" && securityEvent.Event == "pressed")
        {
            await CreatePanicIncidentAsync(securityEvent, cancellationToken);
            return;
        }

        // intrusion / sabotage / anomaly rules
    }

    private async Task CreatePanicIncidentAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        var incident = new Incident
        {
            IncidentType = IncidentType.Panic,
            Status = IncidentStatus.Open,
            Zone = securityEvent.Zone,
            StartedAtUtc = securityEvent.ReceivedAtUtc,
            Explanation = "Panic button was pressed."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEvent = securityEvent
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Incident created -> type={Type}, zone={Zone}, incidentId={IncidentId}",
            incident.IncidentType,
            incident.Zone,
            incident.Id);
    }
}