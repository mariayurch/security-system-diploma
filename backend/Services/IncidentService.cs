using backend.Data;
using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class IncidentService
{
    private static readonly TimeSpan IntrusionConfirmationWindow = TimeSpan.FromSeconds(30);

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

        if (IsPerimeterTrigger(securityEvent))
        {
            await TryCreateSuspectedIntrusionAsync(securityEvent, cancellationToken);
            return;
        }

        if (IsMotionTrigger(securityEvent))
        {
            await TryConfirmIntrusionAsync(securityEvent, cancellationToken);
            return;
        }

        // sabotage / anomaly rules
    }

    private bool IsPerimeterTrigger(SecurityEvent e)
    {
        return e.Armed &&
               e.Sensor == "door" &&
               e.Event == "open";
    }

    private bool IsMotionTrigger(SecurityEvent e)
    {
        return e.Armed &&
               e.Sensor == "motion" &&
               e.Event == "detected";
    }

    private async Task CreatePanicIncidentAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        var incident = new Incident
        {
            IncidentType = IncidentType.Panic,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Confirmed,
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
            "Incident created -> type={Type}, confidence={Confidence}, zone={Zone}, incidentId={IncidentId}",
            incident.IncidentType,
            incident.Confidence,
            incident.Zone,
            incident.Id);
    }

    private async Task TryCreateSuspectedIntrusionAsync(SecurityEvent perimeterEvent, CancellationToken cancellationToken)
    {
        var existingOpenIntrusion = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.Intrusion &&
                i.Zone == perimeterEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.ClosedAtUtc == null &&
                i.StartedAtUtc >= perimeterEvent.ReceivedAtUtc - IntrusionConfirmationWindow)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingOpenIntrusion is not null)
        {
            _logger.LogInformation(
                "Open intrusion already exists for zone {Zone}, skipping new suspected incident",
                perimeterEvent.Zone);
            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.Intrusion,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Suspected,
            Zone = perimeterEvent.Zone,
            StartedAtUtc = perimeterEvent.ReceivedAtUtc,
            Explanation =
                $"Perimeter breach detected: {perimeterEvent.Sensor}/{perimeterEvent.Event} from {perimeterEvent.SensorId} while system was armed."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEvent = perimeterEvent
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Suspected intrusion created -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }

    private async Task TryConfirmIntrusionAsync(SecurityEvent motionEvent, CancellationToken cancellationToken)
    {
        var windowStart = motionEvent.ReceivedAtUtc - IntrusionConfirmationWindow;

        var incident = await _db.Incidents
            .Include(i => i.IncidentEventLinks)
            .ThenInclude(link => link.SecurityEvent)
            .Where(i =>
                i.IncidentType == IncidentType.Intrusion &&
                i.Zone == motionEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.Confidence == IncidentConfidence.Suspected &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= motionEvent.ReceivedAtUtc)
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (incident is null)
        {
            _logger.LogInformation(
                "No suspected intrusion found to confirm for motion event in zone {Zone}",
                motionEvent.Zone);
            return;
        }

        var alreadyLinked = incident.IncidentEventLinks.Any(link => link.SecurityEventId == motionEvent.Id);
        if (!alreadyLinked)
        {
            incident.IncidentEventLinks.Add(new IncidentEventLink
            {
                SecurityEventId = motionEvent.Id
            });
        }

        incident.Confidence = IncidentConfidence.Confirmed;
        incident.Explanation =
            $"{incident.Explanation} Confirmation: motion detected from {motionEvent.SensorId} within 30 seconds.";

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Intrusion confirmed -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }
}