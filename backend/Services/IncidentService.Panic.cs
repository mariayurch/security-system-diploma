using backend.Models;
using backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public partial class IncidentService
{
    private async Task TryCreatePanicIncidentAsync(SecurityEvent panicEvent, CancellationToken cancellationToken)
    {
        var windowStart = panicEvent.ReceivedAtUtc - PanicDedupWindow;

        var existingPanic = await FindOpenIncidentAsync(
            IncidentType.Panic,
            panicEvent.Zone,
            windowStart,
            panicEvent.ReceivedAtUtc,
            confidence: null,
            includeEventLinks: false,
            cancellationToken);

        if (existingPanic is not null)
        {
            _logger.LogInformation(
                "Recent panic incident already exists for zone {Zone}, skipping duplicate panic incident",
                panicEvent.Zone);
            return;
        }

        await CreateIncidentAsync(
            IncidentType.Panic,
            IncidentStatus.Open,
            IncidentConfidence.Confirmed,
            panicEvent.Zone,
            panicEvent.ReceivedAtUtc,
            $"User-triggered panic alarm from {panicEvent.SensorId}. Emergency condition was explicitly reported by the user.",
            new[] { panicEvent },
            cancellationToken);
    }
}
