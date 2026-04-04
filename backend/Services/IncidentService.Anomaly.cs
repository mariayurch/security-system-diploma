using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public partial class IncidentService
{
    private async Task TryCreateMotionAnomalyDuringCooldownAsync(
        SecurityEvent motionEvent,
        CancellationToken cancellationToken)
    {
        var confirmedIntrusion = await GetConfirmedIntrusionForMotionSensorInCooldownAsync(
            motionEvent,
            cancellationToken);

        if (confirmedIntrusion is null)
        {
            return;
        }

        var timeWindowStart = motionEvent.ReceivedAtUtc - MotionAnomalyDuringCooldownWindow;
        var windowStart = Max(confirmedIntrusion.StartedAtUtc, timeWindowStart);

        var recentCount = await CountRecentMatchingEventsAsync(
            motionEvent,
            windowStart,
            cancellationToken);

        if (recentCount < MotionAnomalyDuringCooldownThreshold)
        {
            return;
        }

        var existingIncident = await FindExistingSensorAnomalyAsync(
            motionEvent.Zone,
            motionEvent.SensorId,
            windowStart,
            motionEvent.ReceivedAtUtc,
            cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Motion anomaly already exists during cooldown for sensorId={SensorId} in zone={Zone}",
                motionEvent.SensorId,
                motionEvent.Zone);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.SensorAnomaly,
            IncidentStatus.Open,
            IncidentConfidence.Confirmed,
            motionEvent.Zone,
            motionEvent.ReceivedAtUtc,
            $"Sensor anomaly detected: motion sensor {motionEvent.SensorId} continued triggering after confirmed intrusion and produced {recentCount} events within cooldown monitoring window.",
            new[] { motionEvent },
            cancellationToken);

        _logger.LogInformation(
            "Motion anomaly during cooldown created -> sensorId={SensorId}, zone={Zone}",
            motionEvent.SensorId,
            motionEvent.Zone);
    }

    private async Task TryCreateSensorAnomalyAsync(SecurityEvent securityEvent, CancellationToken cancellationToken)
    {
        var timeWindowStart = securityEvent.ReceivedAtUtc - SensorAnomalyWindow;

        var lastArmedAtUtc = await GetLastArmedAtUtcAsync(
            securityEvent.DeviceId,
            securityEvent.Zone,
            securityEvent.ReceivedAtUtc,
            cancellationToken);

        var windowStart = lastArmedAtUtc.HasValue
            ? Max(lastArmedAtUtc.Value, timeWindowStart)
            : timeWindowStart;

        var threshold = SensorAnomalyThreshold(securityEvent);
        var recentCount = await CountRecentMatchingEventsAsync(securityEvent, windowStart, cancellationToken);

        if (recentCount < threshold)
        {
            return;
        }

        var existingIncident = await FindExistingSensorAnomalyAsync(
            securityEvent.Zone,
            securityEvent.SensorId,
            windowStart,
            securityEvent.ReceivedAtUtc,
            cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Sensor anomaly already exists for sensorId={SensorId} in zone={Zone}",
                securityEvent.SensorId,
                securityEvent.Zone);
            return;
        }

        var relatedEvents = await GetRecentMatchingEventsAsync(securityEvent, windowStart, cancellationToken);

        await CreateIncidentAsync(
            IncidentType.SensorAnomaly,
            IncidentStatus.Open,
            IncidentConfidence.Confirmed,
            securityEvent.Zone,
            relatedEvents.First().ReceivedAtUtc,
            $"Sensor anomaly detected: sensor {securityEvent.SensorId} ({securityEvent.Sensor}/{securityEvent.Event}) produced {recentCount} events within {SensorAnomalyWindow.TotalSeconds:0} seconds (threshold {threshold}).",
            relatedEvents,
            cancellationToken);
    }

    private async Task<int> CountRecentMatchingEventsAsync(
        SecurityEvent securityEvent,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
            .Where(e =>
                e.DeviceId == securityEvent.DeviceId &&
                e.Zone == securityEvent.Zone &&
                e.SensorId == securityEvent.SensorId &&
                e.Sensor == securityEvent.Sensor &&
                e.Event == securityEvent.Event &&
                e.ReceivedAtUtc >= windowStart &&
                e.ReceivedAtUtc <= securityEvent.ReceivedAtUtc)
            .CountAsync(cancellationToken);
    }

    private async Task<List<SecurityEvent>> GetRecentMatchingEventsAsync(
        SecurityEvent securityEvent,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
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
    }

    private async Task<Incident?> FindExistingSensorAnomalyAsync(
        string zone,
        string sensorId,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken cancellationToken)
    {
        return await _db.Incidents
            .Where(i =>
                i.IncidentType == IncidentType.SensorAnomaly &&
                i.Zone == zone &&
                i.Status == IncidentStatus.Open &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= windowEnd &&
                i.Explanation.Contains(sensorId))
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
