using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public partial class IncidentService
{
    private async Task TryCreateOrConfirmSabotageFromTamperAsync(SecurityEvent tamperEvent, CancellationToken cancellationToken)
    {
        var windowStart = await GetCorrelationWindowStartAsync(
            tamperEvent.DeviceId,
            tamperEvent.Zone,
            tamperEvent.ReceivedAtUtc,
            cancellationToken);

        var recentConnectionLost = await FindRecentConnectionLostAsync(tamperEvent, windowStart, cancellationToken);
        if (recentConnectionLost is not null)
        {
            await CreateConfirmedSabotageIncidentAsync(
                tamperEvent.Zone,
                Min(tamperEvent.ReceivedAtUtc, recentConnectionLost.ReceivedAtUtc),
                $"Confirmed sabotage: tamper trigger from {tamperEvent.SensorId} and connection_lost were detected within 30 seconds while system was armed.",
                new[] { tamperEvent, recentConnectionLost },
                cancellationToken);
            return;
        }

        var recentOtherTamper = await FindRecentOtherTamperAsync(tamperEvent, windowStart, cancellationToken);
        if (recentOtherTamper is not null)
        {
            await CreateConfirmedSabotageIncidentAsync(
                tamperEvent.Zone,
                Min(tamperEvent.ReceivedAtUtc, recentOtherTamper.ReceivedAtUtc),
                $"Confirmed sabotage: tamper triggers from {recentOtherTamper.SensorId} and {tamperEvent.SensorId} were detected within 30 seconds while system was armed.",
                new[] { recentOtherTamper, tamperEvent },
                cancellationToken);
            return;
        }

        var existingOpenSabotage = await FindOpenIncidentAsync(
            IncidentType.Sabotage,
            tamperEvent.Zone,
            windowStart,
            tamperEvent.ReceivedAtUtc,
            confidence: null,
            includeEventLinks: false,
            cancellationToken);

        if (existingOpenSabotage is not null)
        {
            _logger.LogInformation(
                "Open sabotage already exists for zone {Zone}, skipping new tamper-based sabotage",
                tamperEvent.Zone);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.Sabotage,
            IncidentStatus.Open,
            IncidentConfidence.Suspected,
            tamperEvent.Zone,
            tamperEvent.ReceivedAtUtc,
            $"Suspected sabotage: tamper trigger from {tamperEvent.SensorId} ({tamperEvent.Sensor}/{tamperEvent.Event}) while system was armed.",
            new[] { tamperEvent },
            cancellationToken);
    }

    private async Task TryCreateOrConfirmSabotageFromConnectionLostAsync(SecurityEvent connectionLostEvent, CancellationToken cancellationToken)
    {
        var windowStart = await GetCorrelationWindowStartAsync(
            connectionLostEvent.DeviceId,
            connectionLostEvent.Zone,
            connectionLostEvent.ReceivedAtUtc,
            cancellationToken);

        var openSabotage = await FindOpenIncidentAsync(
            IncidentType.Sabotage,
            connectionLostEvent.Zone,
            windowStart,
            connectionLostEvent.ReceivedAtUtc,
            confidence: null,
            includeEventLinks: true,
            cancellationToken);

        if (openSabotage is not null)
        {
            await ConfirmSabotageFromConnectionLostAsync(openSabotage, connectionLostEvent, cancellationToken);
            return;
        }

        var recentTamper = await FindRecentTamperAsync(connectionLostEvent, windowStart, cancellationToken);
        if (recentTamper is not null)
        {
            await CreateConfirmedSabotageIncidentAsync(
                connectionLostEvent.Zone,
                Min(recentTamper.ReceivedAtUtc, connectionLostEvent.ReceivedAtUtc),
                $"Confirmed sabotage: tamper trigger from {recentTamper.SensorId} and connection_lost were detected within 30 seconds while system was armed.",
                new[] { recentTamper, connectionLostEvent },
                cancellationToken);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.Sabotage,
            IncidentStatus.Open,
            IncidentConfidence.Suspected,
            connectionLostEvent.Zone,
            connectionLostEvent.ReceivedAtUtc,
            "Suspected sabotage: connection_lost detected while system was armed.",
            new[] { connectionLostEvent },
            cancellationToken);
    }

    private async Task ConfirmSabotageFromConnectionLostAsync(
        Incident openSabotage,
        SecurityEvent connectionLostEvent,
        CancellationToken cancellationToken)
    {
        var alreadyLinked = openSabotage.IncidentEventLinks
            .Any(link => link.SecurityEventId == connectionLostEvent.Id);

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
    }

    private async Task<SecurityEvent?> FindRecentConnectionLostAsync(
        SecurityEvent tamperEvent,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
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
    }

    private async Task<SecurityEvent?> FindRecentOtherTamperAsync(
        SecurityEvent tamperEvent,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
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
    }

    private async Task<SecurityEvent?> FindRecentTamperAsync(
        SecurityEvent connectionLostEvent,
        DateTime windowStart,
        CancellationToken cancellationToken)
    {
        return await _db.SecurityEvents
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
    }

    private async Task CreateConfirmedSabotageIncidentAsync(
        string zone,
        DateTime startedAtUtc,
        string explanation,
        IEnumerable<SecurityEvent> relatedEvents,
        CancellationToken cancellationToken)
    {
        var existingIncident = await FindOpenIncidentAsync(
            IncidentType.Sabotage,
            zone,
            startedAtUtc - IntrusionConfirmationWindow,
            startedAtUtc + IntrusionConfirmationWindow,
            IncidentConfidence.Confirmed,
            includeEventLinks: false,
            cancellationToken);

        if (existingIncident is not null)
        {
            _logger.LogInformation(
                "Confirmed sabotage already exists for zone {Zone}, skipping duplicate confirmed sabotage",
                zone);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.Sabotage,
            IncidentStatus.Open,
            IncidentConfidence.Confirmed,
            zone,
            startedAtUtc,
            explanation,
            relatedEvents,
            cancellationToken);
    }
}
