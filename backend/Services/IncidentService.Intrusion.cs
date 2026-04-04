using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public partial class IncidentService
{
    private async Task TryCreateOrConfirmIntrusionFromPerimeterAsync(SecurityEvent perimeterEvent, CancellationToken cancellationToken)
    {
        var windowStart = await GetCorrelationWindowStartAsync(
            perimeterEvent.DeviceId,
            perimeterEvent.Zone,
            perimeterEvent.ReceivedAtUtc,
            cancellationToken);

        if (await HasConfirmedIntrusionForDoorSensorInCurrentArmedSessionAsync(perimeterEvent, cancellationToken))
        {
            _logger.LogInformation(
                "Door sensor {SensorId} in zone {Zone} already produced a confirmed intrusion in the current armed session; skipping new perimeter intrusion",
                perimeterEvent.SensorId,
                perimeterEvent.Zone);
            return;
        }

        var recentMotion = await FindRecentMotionAsync(perimeterEvent, windowStart, cancellationToken);
        if (recentMotion is not null)
        {
            await CreateConfirmedIntrusionIncidentAsync(
                perimeterEvent.Zone,
                Min(perimeterEvent.ReceivedAtUtc, recentMotion.ReceivedAtUtc),
                $"Confirmed intrusion: perimeter trigger {perimeterEvent.SensorId} ({perimeterEvent.Sensor}/{perimeterEvent.Event}) and motion trigger {recentMotion.SensorId} were detected within 30 seconds while system was armed.",
                new[] { perimeterEvent, recentMotion },
                cancellationToken);
            return;
        }

        var suspectedIntrusion = await FindOpenIncidentAsync(
            IncidentType.Intrusion,
            perimeterEvent.Zone,
            windowStart,
            perimeterEvent.ReceivedAtUtc,
            IncidentConfidence.Suspected,
            includeEventLinks: true,
            cancellationToken);

        if (suspectedIntrusion is not null)
        {
            await ConfirmSuspectedIntrusionFromPerimeterAsync(suspectedIntrusion, perimeterEvent, cancellationToken);
            return;
        }

        var existingOpenIntrusion = await FindOpenIncidentAsync(
            IncidentType.Intrusion,
            perimeterEvent.Zone,
            windowStart,
            perimeterEvent.ReceivedAtUtc,
            confidence: null,
            includeEventLinks: false,
            cancellationToken);

        if (existingOpenIntrusion is not null)
        {
            _logger.LogInformation(
                "Open intrusion already exists for zone {Zone}, skipping new perimeter-based intrusion",
                perimeterEvent.Zone);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.Intrusion,
            IncidentStatus.Open,
            IncidentConfidence.Suspected,
            perimeterEvent.Zone,
            perimeterEvent.ReceivedAtUtc,
            $"Suspected intrusion: perimeter trigger from {perimeterEvent.SensorId} ({perimeterEvent.Sensor}/{perimeterEvent.Event}) while system was armed.",
            new[] { perimeterEvent },
            cancellationToken);
    }

    private async Task TryCreateOrConfirmIntrusionFromMotionAsync(SecurityEvent motionEvent, CancellationToken cancellationToken)
    {
        var windowStart = await GetCorrelationWindowStartAsync(
            motionEvent.DeviceId,
            motionEvent.Zone,
            motionEvent.ReceivedAtUtc,
            cancellationToken);

        if (await IsMotionSensorInCooldownAsync(motionEvent, cancellationToken))
        {
            _logger.LogInformation(
                "Motion sensor {SensorId} in zone {Zone} is in long cooldown; intrusion creation skipped",
                motionEvent.SensorId,
                motionEvent.Zone);

            await TryCreateMotionAnomalyDuringCooldownAsync(motionEvent, cancellationToken);
            return;
        }

        if (await HasRecentConfirmedIntrusionForMotionSensorAsync(motionEvent, cancellationToken))
        {
            _logger.LogInformation(
                "Skipping intrusion confirmation for motion sensor {SensorId} in zone {Zone} due to cooldown",
                motionEvent.SensorId,
                motionEvent.Zone);
            return;
        }

        var suspectedIntrusion = await FindOpenIncidentAsync(
            IncidentType.Intrusion,
            motionEvent.Zone,
            windowStart,
            motionEvent.ReceivedAtUtc,
            IncidentConfidence.Suspected,
            includeEventLinks: true,
            cancellationToken);

        if (suspectedIntrusion is not null)
        {
            await ConfirmSuspectedIntrusionFromMotionAsync(suspectedIntrusion, motionEvent, cancellationToken);
            return;
        }

        var recentPerimeter = await FindRecentPerimeterAsync(motionEvent, windowStart, cancellationToken);
        if (recentPerimeter is not null)
        {
            await CreateConfirmedIntrusionIncidentAsync(
                motionEvent.Zone,
                Min(recentPerimeter.ReceivedAtUtc, motionEvent.ReceivedAtUtc),
                $"Confirmed intrusion: perimeter trigger {recentPerimeter.SensorId} ({recentPerimeter.Sensor}/{recentPerimeter.Event}) and motion trigger {motionEvent.SensorId} were detected within 30 seconds while system was armed.",
                new[] { recentPerimeter, motionEvent },
                cancellationToken);
            return;
        }

        var existingOpenIntrusion = await FindOpenIncidentAsync(
            IncidentType.Intrusion,
            motionEvent.Zone,
            windowStart,
            motionEvent.ReceivedAtUtc,
            confidence: null,
            includeEventLinks: false,
            cancellationToken);

        if (existingOpenIntrusion is not null)
        {
            _logger.LogInformation(
                "Open intrusion already exists for zone {Zone}, skipping new motion-based intrusion",
                motionEvent.Zone);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.Intrusion,
            IncidentStatus.Open,
            IncidentConfidence.Suspected,
            motionEvent.Zone,
            motionEvent.ReceivedAtUtc,
            $"Suspected intrusion: motion detected from {motionEvent.SensorId} while system was armed, but no recent perimeter trigger was found.",
            new[] { motionEvent },
            cancellationToken);
    }

    private async Task ConfirmSuspectedIntrusionFromPerimeterAsync(
        Incident suspectedIntrusion,
        SecurityEvent perimeterEvent,
        CancellationToken cancellationToken)
    {
        var alreadyLinked = suspectedIntrusion.IncidentEventLinks
            .Any(link => link.SecurityEventId == perimeterEvent.Id);

        if (!alreadyLinked)
        {
            suspectedIntrusion.IncidentEventLinks.Add(new IncidentEventLink
            {
                SecurityEventId = perimeterEvent.Id
            });
        }

        suspectedIntrusion.Confidence = IncidentConfidence.Confirmed;
        suspectedIntrusion.Explanation =
            $"{suspectedIntrusion.Explanation} Confirmation: perimeter trigger from {perimeterEvent.SensorId} repeated within 30 seconds.";

        await _db.SaveChangesAsync(cancellationToken);
        await _telegram.SendIncidentUpdatedAsync(suspectedIntrusion, cancellationToken);

        _logger.LogInformation(
            "Intrusion confirmed from repeated perimeter trigger -> zone={Zone}, incidentId={IncidentId}",
            suspectedIntrusion.Zone,
            suspectedIntrusion.Id);
    }

    private async Task ConfirmSuspectedIntrusionFromMotionAsync(
        Incident suspectedIntrusion,
        SecurityEvent motionEvent,
        CancellationToken cancellationToken)
    {
        var alreadyLinked = suspectedIntrusion.IncidentEventLinks
            .Any(link => link.SecurityEventId == motionEvent.Id);

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
        await _telegram.SendIncidentUpdatedAsync(suspectedIntrusion, cancellationToken);

        _logger.LogInformation(
            "Intrusion confirmed from motion event -> zone={Zone}, incidentId={IncidentId}",
            suspectedIntrusion.Zone,
            suspectedIntrusion.Id);
    }

    private async Task<SecurityEvent?> FindRecentMotionAsync(
        SecurityEvent perimeterEvent,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
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
    }

    private async Task<SecurityEvent?> FindRecentPerimeterAsync(
        SecurityEvent motionEvent,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
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
    }

    private async Task CreateConfirmedIntrusionIncidentAsync(
        string zone,
        DateTime startedAtUtc,
        string explanation,
        IEnumerable<SecurityEvent> relatedEvents,
        CancellationToken cancellationToken)
    {
        var existingIncident = await FindOpenIncidentAsync(
            IncidentType.Intrusion,
            zone,
            startedAtUtc - IntrusionConfirmationWindow,
            startedAtUtc + IntrusionConfirmationWindow,
            IncidentConfidence.Confirmed,
            includeEventLinks: false,
            cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Confirmed intrusion already exists for zone {Zone}, skipping duplicate confirmed incident",
                zone);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.Intrusion,
            IncidentStatus.Open,
            IncidentConfidence.Confirmed,
            zone,
            startedAtUtc,
            explanation,
            relatedEvents,
            cancellationToken);
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

        var cooldownStart = Max(motionEvent.ReceivedAtUtc - MotionConfirmedCooldown, armedSessionStart);

        return await HasLinkedSensorAsync(
            IncidentType.Intrusion,
            motionEvent.Zone,
            cooldownStart,
            motionEvent.ReceivedAtUtc,
            IncidentConfidence.Confirmed,
            "motion",
            motionEvent.SensorId,
            cancellationToken);
    }

    private async Task<bool> HasConfirmedIntrusionForDoorSensorInCurrentArmedSessionAsync(
        SecurityEvent perimeterEvent,
        CancellationToken cancellationToken)
    {
        var lastArmedAtUtc = await GetLastArmedAtUtcAsync(
            perimeterEvent.DeviceId,
            perimeterEvent.Zone,
            perimeterEvent.ReceivedAtUtc,
            cancellationToken);

        if (!lastArmedAtUtc.HasValue)
        {
            return false;
        }

        return await HasLinkedSensorAsync(
            IncidentType.Intrusion,
            perimeterEvent.Zone,
            lastArmedAtUtc.Value,
            perimeterEvent.ReceivedAtUtc,
            IncidentConfidence.Confirmed,
            "door",
            perimeterEvent.SensorId,
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
        if (lastArmedAtUtc.HasValue)
        {
            cooldownStart = Max(cooldownStart, lastArmedAtUtc.Value);
        }

        return await HasLinkedSensorAsync(
            IncidentType.Intrusion,
            motionEvent.Zone,
            cooldownStart,
            motionEvent.ReceivedAtUtc,
            IncidentConfidence.Confirmed,
            "motion",
            motionEvent.SensorId,
            cancellationToken);
    }

    private async Task<Incident?> GetConfirmedIntrusionForMotionSensorInCooldownAsync(
        SecurityEvent motionEvent,
        CancellationToken cancellationToken)
    {
        var lastArmedAtUtc = await GetLastArmedAtUtcAsync(
            motionEvent.DeviceId,
            motionEvent.Zone,
            motionEvent.ReceivedAtUtc,
            cancellationToken);

        var cooldownStart = motionEvent.ReceivedAtUtc - MotionConfirmedCooldown;
        if (lastArmedAtUtc.HasValue)
        {
            cooldownStart = Max(cooldownStart, lastArmedAtUtc.Value);
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
            .Where(i =>
                i.IncidentEventLinks.Any(link =>
                    link.SecurityEvent != null &&
                    link.SecurityEvent.Sensor == "motion" &&
                    link.SecurityEvent.SensorId == motionEvent.SensorId))
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
