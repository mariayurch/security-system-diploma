using backend.Data;
using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class IncidentService
{
    private static readonly TimeSpan IntrusionConfirmationWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PanicDedupWindow = TimeSpan.FromSeconds(30);

    private readonly AppDbContext _db;
    private readonly ILogger<IncidentService> _logger;

    public IncidentService(AppDbContext db, ILogger<IncidentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessEventAsync(SecurityEvent securityEvent, CancellationToken cancellationToken = default)
    {
        if (IsPanicTrigger(securityEvent))
        {
            await TryCreatePanicIncidentAsync(securityEvent, cancellationToken);
            return;
        }

        if (IsPerimeterTrigger(securityEvent))
        {
            await TryCreateOrConfirmIntrusionFromPerimeterAsync(securityEvent, cancellationToken);
            return;
        }

        if (IsMotionTrigger(securityEvent))
        {
            await TryCreateOrConfirmIntrusionFromMotionAsync(securityEvent, cancellationToken);
            return;
        }

        // sabotage / anomaly rules later
    }

    private bool IsPanicTrigger(SecurityEvent e)
    {
        return e.Sensor == "panic_button" && e.Event == "pressed";
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

    private async Task TryCreatePanicIncidentAsync(SecurityEvent panicEvent, CancellationToken cancellationToken)
    {
        var windowStart = panicEvent.ReceivedAtUtc - PanicDedupWindow;

        var existingPanic = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.Panic &&
                i.Zone == panicEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= panicEvent.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingPanic is not null)
        {
            _logger.LogInformation(
                "Recent panic incident already exists for zone {Zone}, skipping duplicate panic incident",
                panicEvent.Zone);
            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.Panic,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Confirmed,
            Zone = panicEvent.Zone,
            StartedAtUtc = panicEvent.ReceivedAtUtc,
            Explanation =
                $"User-triggered panic alarm from {panicEvent.SensorId}. Emergency condition was explicitly reported by the user."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEvent = panicEvent
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Panic incident created -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }

    private async Task TryCreateOrConfirmIntrusionFromPerimeterAsync(SecurityEvent perimeterEvent, CancellationToken cancellationToken)
    {
        var windowStart = perimeterEvent.ReceivedAtUtc - IntrusionConfirmationWindow;

        var recentMotion = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == perimeterEvent.DeviceId &&
                e.Zone == perimeterEvent.Zone &&
                e.Armed &&
                e.Sensor == "motion" &&
                e.Event == "detected" &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= perimeterEvent.ReceivedAtUtc)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentMotion is not null)
        {
            await CreateConfirmedIntrusionIncidentAsync(
                zone: perimeterEvent.Zone,
                startedAtUtc: perimeterEvent.ReceivedAtUtc < recentMotion.ReceivedAtUtc
                    ? perimeterEvent.ReceivedAtUtc
                    : recentMotion.ReceivedAtUtc,
                explanation:
                    $"Confirmed intrusion: perimeter trigger {perimeterEvent.SensorId} ({perimeterEvent.Sensor}/{perimeterEvent.Event}) " +
                    $"and motion trigger {recentMotion.SensorId} were detected within 30 seconds while system was armed.",
                relatedEvents: new[] { perimeterEvent, recentMotion },
                cancellationToken: cancellationToken);

            return;
        }

        var existingOpenIntrusion = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.Intrusion &&
                i.Zone == perimeterEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= perimeterEvent.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingOpenIntrusion is not null)
        {
            _logger.LogInformation(
                "Open intrusion already exists for zone {Zone}, skipping new perimeter-based intrusion",
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
                $"Suspected intrusion: perimeter trigger from {perimeterEvent.SensorId} ({perimeterEvent.Sensor}/{perimeterEvent.Event}) while system was armed."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEvent = perimeterEvent
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Suspected intrusion created from perimeter trigger -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }

    private async Task TryCreateOrConfirmIntrusionFromMotionAsync(SecurityEvent motionEvent, CancellationToken cancellationToken)
    {
        var windowStart = motionEvent.ReceivedAtUtc - IntrusionConfirmationWindow;

        var suspectedIntrusion = await _db.Incidents
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

        if (suspectedIntrusion is not null)
        {
            var alreadyLinked = suspectedIntrusion.IncidentEventLinks.Any(link => link.SecurityEventId == motionEvent.Id);
            if (!alreadyLinked)
            {
                suspectedIntrusion.IncidentEventLinks.Add(new IncidentEventLink
                {
                    SecurityEventId = motionEvent.Id
                });
            }

            suspectedIntrusion.Confidence = IncidentConfidence.Confirmed;
            suspectedIntrusion.Explanation =
                $"{suspectedIntrusion.Explanation} Confirmation: motion detected from {motionEvent.SensorId} within 30 seconds.";

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Intrusion confirmed from motion event -> zone={Zone}, incidentId={IncidentId}",
                suspectedIntrusion.Zone,
                suspectedIntrusion.Id);

            return;
        }

        var recentPerimeter = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == motionEvent.DeviceId &&
                e.Zone == motionEvent.Zone &&
                e.Armed &&
                e.Sensor == "door" &&
                e.Event == "open" &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= motionEvent.ReceivedAtUtc)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentPerimeter is not null)
        {
            await CreateConfirmedIntrusionIncidentAsync(
                zone: motionEvent.Zone,
                startedAtUtc: recentPerimeter.ReceivedAtUtc < motionEvent.ReceivedAtUtc
                    ? recentPerimeter.ReceivedAtUtc
                    : motionEvent.ReceivedAtUtc,
                explanation:
                    $"Confirmed intrusion: perimeter trigger {recentPerimeter.SensorId} ({recentPerimeter.Sensor}/{recentPerimeter.Event}) " +
                    $"and motion trigger {motionEvent.SensorId} were detected within 30 seconds while system was armed.",
                relatedEvents: new[] { recentPerimeter, motionEvent },
                cancellationToken: cancellationToken);

            return;
        }

        var existingOpenIntrusion = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.Intrusion &&
                i.Zone == motionEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= motionEvent.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingOpenIntrusion is not null)
        {
            _logger.LogInformation(
                "Open intrusion already exists for zone {Zone}, skipping new motion-based intrusion",
                motionEvent.Zone);
            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.Intrusion,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Suspected,
            Zone = motionEvent.Zone,
            StartedAtUtc = motionEvent.ReceivedAtUtc,
            Explanation =
                $"Suspected intrusion: motion detected from {motionEvent.SensorId} while system was armed, but no recent perimeter trigger was found."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEvent = motionEvent
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Suspected intrusion created from motion trigger -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }

    private async Task CreateConfirmedIntrusionIncidentAsync(
        string zone,
        DateTime startedAtUtc,
        string explanation,
        IEnumerable<SecurityEvent> relatedEvents,
        CancellationToken cancellationToken)
    {
        var existingIncident = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.Intrusion &&
                i.Zone == zone &&
                i.Status == IncidentStatus.Open &&
                i.Confidence == IncidentConfidence.Confirmed &&
                i.StartedAtUtc >= startedAtUtc - IntrusionConfirmationWindow &&
                i.StartedAtUtc <= startedAtUtc + IntrusionConfirmationWindow)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Confirmed intrusion already exists for zone {Zone}, skipping duplicate confirmed incident",
                zone);
            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.Intrusion,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Confirmed,
            Zone = zone,
            StartedAtUtc = startedAtUtc,
            Explanation = explanation
        };

        foreach (var securityEvent in relatedEvents)
        {
            incident.IncidentEventLinks.Add(new IncidentEventLink
            {
                SecurityEventId = securityEvent.Id
            });
        }

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Confirmed intrusion incident created -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }
}