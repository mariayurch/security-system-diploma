using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public partial class IncidentService
{
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

        return lastArmedAtUtc.HasValue && lastArmedAtUtc.Value > timeWindowStart
            ? lastArmedAtUtc.Value
            : timeWindowStart;
    }

    private async Task CreateIncidentAsync(
        IncidentType incidentType,
        IncidentStatus status,
        IncidentConfidence confidence,
        string zone,
        DateTime startedAtUtc,
        string explanation,
        IEnumerable<SecurityEvent> relatedEvents,
        CancellationToken cancellationToken)
    {
        var incident = new Incident
        {
            IncidentType = incidentType,
            Status = status,
            Confidence = confidence,
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
            "{IncidentType} incident created -> zone={Zone}, incidentId={IncidentId}, confidence={Confidence}",
            incident.IncidentType,
            incident.Zone,
            incident.Id,
            incident.Confidence);
    }

    private async Task<Incident?> FindOpenIncidentAsync(
        IncidentType incidentType,
        string zone,
        DateTime windowStart,
        DateTime windowEnd,
        IncidentConfidence? confidence,
        bool includeEventLinks,
        CancellationToken cancellationToken)
    {
        IQueryable<Incident> query = _db.Incidents;

        if (includeEventLinks)
        {
            query = query
                .Include(i => i.IncidentEventLinks)
                .ThenInclude(link => link.SecurityEvent);
        }

        query = query.Where(i =>
            i.IncidentType == incidentType &&
            i.Zone == zone &&
            i.Status == IncidentStatus.Open &&
            i.StartedAtUtc >= windowStart &&
            i.StartedAtUtc <= windowEnd);

        if (confidence.HasValue)
        {
            query = query.Where(i => i.Confidence == confidence.Value);
        }

        return await query
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> HasLinkedSensorAsync(
        IncidentType incidentType,
        string zone,
        DateTime windowStart,
        DateTime windowEnd,
        IncidentConfidence confidence,
        string sensor,
        string sensorId,
        CancellationToken cancellationToken)
    {
        return await _db.Incidents
            .Include(i => i.IncidentEventLinks)
            .ThenInclude(link => link.SecurityEvent)
            .Where(i =>
                i.IncidentType == incidentType &&
                i.Zone == zone &&
                i.Status == IncidentStatus.Open &&
                i.Confidence == confidence &&
                i.StartedAtUtc >= windowStart &&
                i.StartedAtUtc <= windowEnd)
            .AnyAsync(i =>
                i.IncidentEventLinks.Any(link =>
                    link.SecurityEvent != null &&
                    link.SecurityEvent.Sensor == sensor &&
                    link.SecurityEvent.SensorId == sensorId),
                cancellationToken);
    }

    private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
    private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
}
