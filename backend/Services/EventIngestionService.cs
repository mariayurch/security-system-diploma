using backend.Data;
using backend.Dtos;
using backend.Models;
using Microsoft.EntityFrameworkCore;

namespace backend.Services;

public class EventIngestionService
{
    private readonly AppDbContext _db;
    private readonly IncidentService _incidentService;
    private readonly ILogger<EventIngestionService> _logger;

    public EventIngestionService(
        AppDbContext db,
        IncidentService incidentService,
        ILogger<EventIngestionService> logger)
    {
        _db = db;
        _incidentService = incidentService;
        _logger = logger;
    }

    public async Task<SecurityEvent?> SaveEventAsync(EspEventDto dto, CancellationToken cancellationToken = default)
    {
        var entity = new SecurityEvent
        {
            EventId = dto.EventId,
            DeviceId = dto.DeviceId,
            Zone = dto.Zone,
            Sensor = dto.Sensor,
            Event = dto.Event,
            Armed = dto.Armed,
            Rssi = dto.Rssi,
            Ts = dto.Ts,
            ReceivedAtUtc = DateTime.UtcNow
        };

        _db.SecurityEvents.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Event saved -> device={DeviceId}, sensor={Sensor}, event={Event}, eventId={EventId}",
                entity.DeviceId,
                entity.Sensor,
                entity.Event,
                entity.EventId);

            await _incidentService.ProcessEventAsync(entity, cancellationToken);

            return entity;
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning(
                "Duplicate event ignored -> device={DeviceId}, eventId={EventId}",
                entity.DeviceId,
                entity.EventId);

            return null;
        }
    }
}