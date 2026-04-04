using backend.Data;
using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class IncidentService
{
    private static readonly TimeSpan IntrusionConfirmationWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PanicDedupWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SensorAnomalyWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MotionConfirmedCooldown = TimeSpan.FromHours(2);
    private static readonly TimeSpan MotionAnomalyDuringCooldownWindow = TimeSpan.FromMinutes(5);
    private const int MotionAnomalyDuringCooldownThreshold = 5; 
    private int SensorAnomalyThreshold(SecurityEvent e)
    {
        if (e.Sensor == "motion")
        {
            return 5;
        }

        return 10;
    }

    private readonly AppDbContext _db;
    private readonly ILogger<IncidentService> _logger;

    private bool IsAnomalyCandidate(SecurityEvent e)
    {
        return e.Sensor == "door" ||
            e.Sensor == "motion" ||
            e.Sensor == "door_tamper" ||
            e.Sensor == "motion_tamper";
    }

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
        }

        if (IsMotionTrigger(securityEvent))
        {
            await TryCreateOrConfirmIntrusionFromMotionAsync(securityEvent, cancellationToken);
        }

        if (IsTamperTrigger(securityEvent))
        {
            await TryCreateOrConfirmSabotageFromTamperAsync(securityEvent, cancellationToken);
        }

        if (IsConnectionLostTrigger(securityEvent))
        {
            await TryCreateOrConfirmSabotageFromConnectionLostAsync(securityEvent, cancellationToken);
        }

        if (IsAnomalyCandidate(securityEvent))
        {
            await TryCreateSensorAnomalyAsync(securityEvent, cancellationToken);
        }
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

    private bool IsTamperTrigger(SecurityEvent e)
    {
        return e.Armed &&
            (e.Sensor == "door_tamper" || e.Sensor == "motion_tamper") &&
            e.Event == "triggered";
    }

    private bool IsConnectionLostTrigger(SecurityEvent e)
    {
        return e.Armed &&
            e.Sensor == "system" &&
            e.Event == "connection_lost";
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
        var windowStart = await GetCorrelationWindowStartAsync(
            perimeterEvent.DeviceId,
            perimeterEvent.Zone,
            perimeterEvent.ReceivedAtUtc,
            cancellationToken);

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
        var windowStart = await GetCorrelationWindowStartAsync(
            motionEvent.DeviceId,
            motionEvent.Zone,
            motionEvent.ReceivedAtUtc,
            cancellationToken);

        var isInCooldown = await IsMotionSensorInCooldownAsync(
            motionEvent,
            cancellationToken);

        if (isInCooldown)
        {
            _logger.LogInformation(
                "Motion sensor {SensorId} in zone {Zone} is in long cooldown; intrusion creation skipped",
                motionEvent.SensorId,
                motionEvent.Zone);

            await TryCreateMotionAnomalyDuringCooldownAsync(motionEvent, cancellationToken);
            return;
        }

        var hasRecentConfirmedIntrusion = await HasRecentConfirmedIntrusionForMotionSensorAsync(
                motionEvent,
                cancellationToken);

        if (hasRecentConfirmedIntrusion)
        {
            _logger.LogInformation(
                "Skipping intrusion confirmation for motion sensor {SensorId} in zone {Zone} due to cooldown",
                motionEvent.SensorId,
                motionEvent.Zone);
            return;
        }

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

    private async Task<DateTime?> GetLastArmedAtUtcAsync(
        string deviceId,
        string zone,
        DateTime beforeUtc,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == deviceId &&
                e.Zone == zone &&
                e.Sensor == "system" &&
                e.Event == "armed" &&
                e.ReceivedAtUtc <= beforeUtc)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .Select(e => (DateTime?)e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<DateTime> GetCorrelationWindowStartAsync(
        string deviceId,
        string zone,
        DateTime eventTimeUtc,
        CancellationToken cancellationToken)
    {
        var timeWindowStart = eventTimeUtc - IntrusionConfirmationWindow;

        var lastArmedAtUtc = await GetLastArmedAtUtcAsync(
            deviceId,
            zone,
            eventTimeUtc,
            cancellationToken);

        if (lastArmedAtUtc.HasValue && lastArmedAtUtc.Value > timeWindowStart)
        {
            return lastArmedAtUtc.Value;
        }

        return timeWindowStart;
    }

    private async Task TryCreateOrConfirmSabotageFromTamperAsync(SecurityEvent tamperEvent, CancellationToken cancellationToken)
    {
        var windowStart = await GetCorrelationWindowStartAsync(
            tamperEvent.DeviceId,
            tamperEvent.Zone,
            tamperEvent.ReceivedAtUtc,
            cancellationToken);

        var recentConnectionLost = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == tamperEvent.DeviceId &&
                e.Zone == tamperEvent.Zone &&
                e.Armed &&
                e.Sensor == "system" &&
                e.Event == "connection_lost" &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= tamperEvent.ReceivedAtUtc)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentConnectionLost is not null)
        {
            await CreateConfirmedSabotageIncidentAsync(
                zone: tamperEvent.Zone,
                startedAtUtc: tamperEvent.ReceivedAtUtc < recentConnectionLost.ReceivedAtUtc
                    ? tamperEvent.ReceivedAtUtc
                    : recentConnectionLost.ReceivedAtUtc,
                explanation:
                    $"Confirmed sabotage: tamper trigger from {tamperEvent.SensorId} and connection_lost were detected within 30 seconds while system was armed.",
                relatedEvents: new[] { tamperEvent, recentConnectionLost },
                cancellationToken: cancellationToken);

            return;
        }

        var recentOtherTamper = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == tamperEvent.DeviceId &&
                e.Zone == tamperEvent.Zone &&
                e.Armed &&
                (e.Sensor == "door_tamper" || e.Sensor == "motion_tamper") &&
                e.Event == "triggered" &&
                e.SensorId != tamperEvent.SensorId &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= tamperEvent.ReceivedAtUtc)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentOtherTamper is not null)
        {
            await CreateConfirmedSabotageIncidentAsync(
                zone: tamperEvent.Zone,
                startedAtUtc: tamperEvent.ReceivedAtUtc < recentOtherTamper.ReceivedAtUtc
                    ? tamperEvent.ReceivedAtUtc
                    : recentOtherTamper.ReceivedAtUtc,
                explanation:
                    $"Confirmed sabotage: tamper triggers from {recentOtherTamper.SensorId} and {tamperEvent.SensorId} were detected within 30 seconds while system was armed.",
                relatedEvents: new[] { recentOtherTamper, tamperEvent },
                cancellationToken: cancellationToken);

            return;
        }

        var existingOpenSabotage = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.Sabotage &&
                i.Zone == tamperEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= tamperEvent.ReceivedAtUtc)
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingOpenSabotage is not null)
        {
            _logger.LogInformation(
                "Open sabotage already exists for zone {Zone}, skipping new tamper-based sabotage",
                tamperEvent.Zone);
            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.Sabotage,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Suspected,
            Zone = tamperEvent.Zone,
            StartedAtUtc = tamperEvent.ReceivedAtUtc,
            Explanation =
                $"Suspected sabotage: tamper trigger from {tamperEvent.SensorId} ({tamperEvent.Sensor}/{tamperEvent.Event}) while system was armed."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEvent = tamperEvent
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Suspected sabotage created -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }

    private async Task TryCreateOrConfirmSabotageFromConnectionLostAsync(SecurityEvent connectionLostEvent, CancellationToken cancellationToken)
    {
        var windowStart = await GetCorrelationWindowStartAsync(
            connectionLostEvent.DeviceId,
            connectionLostEvent.Zone,
            connectionLostEvent.ReceivedAtUtc,
            cancellationToken);

        var openSabotage = await _db.Incidents
            .Include(i => i.IncidentEventLinks)
            .ThenInclude(link => link.SecurityEvent)
            .Where(i =>
                i.IncidentType == IncidentType.Sabotage &&
                i.Zone == connectionLostEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= connectionLostEvent.ReceivedAtUtc)
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (openSabotage is not null)
        {
            var alreadyLinked = openSabotage.IncidentEventLinks.Any(link => link.SecurityEventId == connectionLostEvent.Id);
            if (!alreadyLinked)
            {
                openSabotage.IncidentEventLinks.Add(new IncidentEventLink
                {
                    SecurityEventId = connectionLostEvent.Id
                });
            }

            openSabotage.Confidence = IncidentConfidence.Confirmed;
            openSabotage.Explanation =
                $"{openSabotage.Explanation} Confirmation: connection_lost detected within 30 seconds.";

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Sabotage confirmed from connection_lost -> zone={Zone}, incidentId={IncidentId}",
                openSabotage.Zone,
                openSabotage.Id);

            return;
        }

        var recentTamper = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == connectionLostEvent.DeviceId &&
                e.Zone == connectionLostEvent.Zone &&
                e.Armed &&
                (e.Sensor == "door_tamper" || e.Sensor == "motion_tamper") &&
                e.Event == "triggered" &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= connectionLostEvent.ReceivedAtUtc)
            .OrderByDescending(e => e.ReceivedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentTamper is not null)
        {
            await CreateConfirmedSabotageIncidentAsync(
                zone: connectionLostEvent.Zone,
                startedAtUtc: recentTamper.ReceivedAtUtc < connectionLostEvent.ReceivedAtUtc
                    ? recentTamper.ReceivedAtUtc
                    : connectionLostEvent.ReceivedAtUtc,
                explanation:
                    $"Confirmed sabotage: tamper trigger from {recentTamper.SensorId} and connection_lost were detected within 30 seconds while system was armed.",
                relatedEvents: new[] { recentTamper, connectionLostEvent },
                cancellationToken: cancellationToken);

            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.Sabotage,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Suspected,
            Zone = connectionLostEvent.Zone,
            StartedAtUtc = connectionLostEvent.ReceivedAtUtc,
            Explanation =
                $"Suspected sabotage: connection_lost detected while system was armed."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEvent = connectionLostEvent
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Suspected sabotage created from connection_lost -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }

    private async Task CreateConfirmedSabotageIncidentAsync(
    string zone,
    DateTime startedAtUtc,
    string explanation,
    IEnumerable<SecurityEvent> relatedEvents,
    CancellationToken cancellationToken)
    {
        var existingIncident = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.Sabotage &&
                i.Zone == zone &&
                i.Status == IncidentStatus.Open &&
                i.Confidence == IncidentConfidence.Confirmed &&
                i.StartedAtUtc >= startedAtUtc - IntrusionConfirmationWindow &&
                i.StartedAtUtc <= startedAtUtc + IntrusionConfirmationWindow)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Confirmed sabotage already exists for zone {Zone}, skipping duplicate confirmed sabotage",
                zone);
            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.Sabotage,
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
            "Confirmed sabotage incident created -> zone={Zone}, incidentId={IncidentId}",
            incident.Zone,
            incident.Id);
    }

    private async Task<bool> HasRecentConfirmedIntrusionForMotionSensorAsync(
    SecurityEvent motionEvent,
    CancellationToken cancellationToken)
    {
        var armedSessionStart = await GetCorrelationWindowStartAsync(
            motionEvent.DeviceId,
            motionEvent.Zone,
            motionEvent.ReceivedAtUtc,
            cancellationToken);

        var cooldownStart = motionEvent.ReceivedAtUtc - MotionConfirmedCooldown;
        if (armedSessionStart > cooldownStart)
        {
            cooldownStart = armedSessionStart;
        }

        return await _db.Incidents
            .Include(i => i.IncidentEventLinks)
            .ThenInclude(link => link.SecurityEvent)
            .Where(i =>
                i.IncidentType == IncidentType.Intrusion &&
                i.Zone == motionEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.Confidence == IncidentConfidence.Confirmed &&
                i.StartedAtUtc >= cooldownStart &&
                i.StartedAtUtc <= motionEvent.ReceivedAtUtc)
            .AnyAsync(i =>
                i.IncidentEventLinks.Any(link =>
                    link.SecurityEvent != null &&
                    link.SecurityEvent.Sensor == "motion" &&
                    link.SecurityEvent.SensorId == motionEvent.SensorId),
                cancellationToken);
    }

    private async Task<bool> IsMotionSensorInCooldownAsync(
        SecurityEvent motionEvent,
        CancellationToken cancellationToken)
    {
        var lastArmedAtUtc = await GetLastArmedAtUtcAsync(
            motionEvent.DeviceId,
            motionEvent.Zone,
            motionEvent.ReceivedAtUtc,
            cancellationToken);

        var cooldownStart = motionEvent.ReceivedAtUtc - MotionConfirmedCooldown;

        if (lastArmedAtUtc.HasValue && lastArmedAtUtc.Value > cooldownStart)
        {
            cooldownStart = lastArmedAtUtc.Value;
        }

        return await _db.Incidents
            .Include(i => i.IncidentEventLinks)
            .ThenInclude(link => link.SecurityEvent)
            .Where(i =>
                i.IncidentType == IncidentType.Intrusion &&
                i.Zone == motionEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.Confidence == IncidentConfidence.Confirmed &&
                i.StartedAtUtc >= cooldownStart &&
                i.StartedAtUtc <= motionEvent.ReceivedAtUtc)
            .AnyAsync(i =>
                i.IncidentEventLinks.Any(link =>
                    link.SecurityEvent != null &&
                    link.SecurityEvent.Sensor == "motion" &&
                    link.SecurityEvent.SensorId == motionEvent.SensorId),
                cancellationToken);
    }

    private async Task TryCreateMotionAnomalyDuringCooldownAsync(
        SecurityEvent motionEvent,
        CancellationToken cancellationToken)
    {
        var windowStart = motionEvent.ReceivedAtUtc - MotionAnomalyDuringCooldownWindow;

        var recentCount = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == motionEvent.DeviceId &&
                e.Zone == motionEvent.Zone &&
                e.SensorId == motionEvent.SensorId &&
                e.Sensor == "motion" &&
                e.Event == "detected" &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= motionEvent.ReceivedAtUtc)
            .CountAsync(cancellationToken);

        if (recentCount < MotionAnomalyDuringCooldownThreshold)
        {
            return;
        }

        var existingIncident = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.SensorAnomaly &&
                i.Zone == motionEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= motionEvent.ReceivedAtUtc &&
                i.Explanation.Contains(motionEvent.SensorId))
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Motion anomaly already exists during cooldown for sensorId={SensorId} in zone={Zone}",
                motionEvent.SensorId,
                motionEvent.Zone);
            return;
        }

        var incident = new Incident
        {
            IncidentType = IncidentType.SensorAnomaly,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Confirmed,
            Zone = motionEvent.Zone,
            StartedAtUtc = motionEvent.ReceivedAtUtc,
            Explanation =
                $"Sensor anomaly detected: motion sensor {motionEvent.SensorId} continued triggering during intrusion cooldown and produced {recentCount} events within {MotionAnomalyDuringCooldownWindow.TotalMinutes:0} minutes."
        };

        incident.IncidentEventLinks.Add(new IncidentEventLink
        {
            SecurityEventId = motionEvent.Id
        });

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Motion anomaly during cooldown created -> sensorId={SensorId}, zone={Zone}, incidentId={IncidentId}",
            motionEvent.SensorId,
            motionEvent.Zone,
            incident.Id);
    }

    private async Task TryCreateSensorAnomalyAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        var timeWindowStart = securityEvent.ReceivedAtUtc - SensorAnomalyWindow;

        var lastArmedAtUtc = await GetLastArmedAtUtcAsync(
            securityEvent.DeviceId,
            securityEvent.Zone,
            securityEvent.ReceivedAtUtc,
            cancellationToken);

        var windowStart = lastArmedAtUtc.HasValue && lastArmedAtUtc.Value > timeWindowStart
            ? lastArmedAtUtc.Value
            : timeWindowStart;
            
        var threshold = SensorAnomalyThreshold(securityEvent);

        var recentCount = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == securityEvent.DeviceId &&
                e.Zone == securityEvent.Zone &&
                e.SensorId == securityEvent.SensorId &&
                e.Sensor == securityEvent.Sensor &&
                e.Event == securityEvent.Event &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= securityEvent.ReceivedAtUtc)
            .CountAsync(cancellationToken);

        if (recentCount < threshold)
        {
            return;
        }

        var existingIncident = await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.SensorAnomaly &&
                i.Zone == securityEvent.Zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= securityEvent.ReceivedAtUtc &&
                i.Explanation.Contains(securityEvent.SensorId))
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Sensor anomaly already exists for sensorId={SensorId} in zone={Zone}",
                securityEvent.SensorId,
                securityEvent.Zone);
            return;
        }

        var relatedEvents = await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == securityEvent.DeviceId &&
                e.Zone == securityEvent.Zone &&
                e.SensorId == securityEvent.SensorId &&
                e.Sensor == securityEvent.Sensor &&
                e.Event == securityEvent.Event &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= securityEvent.ReceivedAtUtc)
            .OrderBy(e => e.ReceivedAtUtc)
            .ToListAsync(cancellationToken);

        var incident = new Incident
        {
            IncidentType = IncidentType.SensorAnomaly,
            Status = IncidentStatus.Open,
            Confidence = IncidentConfidence.Confirmed,
            Zone = securityEvent.Zone,
            StartedAtUtc = relatedEvents.First().ReceivedAtUtc,
            Explanation =
            $"Sensor anomaly detected: sensor {securityEvent.SensorId} ({securityEvent.Sensor}/{securityEvent.Event}) produced {recentCount} events within {SensorAnomalyWindow.TotalSeconds:0} seconds (threshold {threshold})."
        };

        foreach (var relatedEvent in relatedEvents)
        {
            incident.IncidentEventLinks.Add(new IncidentEventLink
            {
                SecurityEventId = relatedEvent.Id
            });
        }

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Sensor anomaly incident created -> sensorId={SensorId}, zone={Zone}, incidentId={IncidentId}",
            securityEvent.SensorId,
            incident.Zone,
            incident.Id);
    }
}