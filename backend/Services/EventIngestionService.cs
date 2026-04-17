using backend.Data;
using backend.Dtos;
using backend.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        
        if (string.IsNullOrWhiteSpace(dto.DeviceId))
        {
            _logger.LogWarning(
                "Invalid event payload ignored: DeviceId is missing. BootId={BootId}, EventId={EventId}, Sensor={Sensor}, Event={Event}",
                dto.BootId,
                dto.EventId,
                dto.Sensor,
                dto.Event);

            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.BootId))
        {
            _logger.LogWarning(
                "Invalid event payload ignored: BootId is missing. DeviceId={DeviceId}, EventId={EventId}, Sensor={Sensor}, Event={Event}",
                dto.DeviceId,
                dto.EventId,
                dto.Sensor,
                dto.Event);

            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.SensorId))
        {
            _logger.LogWarning(
                "Invalid event payload ignored: SensorId is missing. DeviceId={DeviceId}, BootId={BootId}, EventId={EventId}",
                dto.DeviceId,
                dto.BootId,
                dto.EventId);

            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.Sensor))
        {
            _logger.LogWarning(
                "Invalid event payload ignored: Sensor is missing. DeviceId={DeviceId}, BootId={BootId}, EventId={EventId}",
                dto.DeviceId,
                dto.BootId,
                dto.EventId);

            return null;
        }

        if (string.IsNullOrWhiteSpace(dto.Event))
        {
            _logger.LogWarning(
                "Invalid event payload ignored: Event is missing. DeviceId={DeviceId}, BootId={BootId}, EventId={EventId}",
                dto.DeviceId,
                dto.BootId,
                dto.EventId);

            return null;
        }

        var alreadyExists = await _db.SecurityEvents.AnyAsync(
            e => e.DeviceId == dto.DeviceId
            && e.BootId == dto.BootId
            && e.EventId == dto.EventId,
            cancellationToken);

        if (alreadyExists)
        {
            _logger.LogWarning(
                "Duplicate event ignored -> device={DeviceId}, bootId={BootId}, eventId={EventId}",
                dto.DeviceId,
                dto.BootId,
                dto.EventId);

            return null;
        }

        var entity = new SecurityEvent
        {
            BootId = dto.BootId,
            EventId = dto.EventId,
            DeviceId = dto.DeviceId,
            Zone = dto.Zone,
            SensorId = dto.SensorId,
            Sensor = dto.Sensor,
            Event = dto.Event,
            Armed = dto.Armed,
            Rssi = dto.Rssi,
            Ts = dto.Ts,
            ReceivedAtUtc = DateTime.UtcNow
        };

        try
        {
            _db.SecurityEvents.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Event saved -> device={DeviceId}, sensor={Sensor}, event={Event}, eventId={EventId}",
                entity.DeviceId,
                entity.Sensor,
                entity.Event,
                entity.EventId);

            return entity;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(
                "Duplicate event ignored on save -> device={DeviceId}, bootId={BootId}, eventId={EventId}",
                dto.DeviceId,
                dto.BootId,
                dto.EventId);

            return null;
        }
        catch (DbUpdateException ex) when (IsNotNullViolation(ex))
        {
            _logger.LogWarning(
                "Invalid event ignored on save due to null required field -> device={DeviceId}, bootId={BootId}, eventId={EventId}",
                dto.DeviceId,
                dto.BootId,
                dto.EventId);

            return null;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx
            && pgEx.SqlState == "23505";
    }

    private static bool IsNotNullViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx
            && pgEx.SqlState == "23502";
    }
}