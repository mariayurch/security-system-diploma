using backend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Services.Telegram;
using backend.Dtos;
using backend.Models.Enums;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IncidentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITelegramNotificationService _telegram;

    public IncidentsController(AppDbContext db, ITelegramNotificationService telegram)
    {
        _db = db;
        _telegram = telegram;
    }

    [HttpGet]
    public async Task<IActionResult> GetIncidents([FromQuery] int limit = 20)
    {
        var incidents = await _db.Incidents
            .OrderByDescending(i => i.Id)
            .Take(limit)
            .Select(i => new IncidentListItemDto
            {
                Id = i.Id,
                IncidentType = i.IncidentType.ToString(),
                Status = i.Status.ToString(),
                Confidence = i.Confidence.ToString(),
                Zone = i.Zone,
                StartedAtUtc = i.StartedAtUtc,
                ClosedAtUtc = i.ClosedAtUtc,
                Explanation = i.Explanation
            })
            .ToListAsync();

        return Ok(incidents);
    }

    [HttpPatch("{id:int}/ack")]
    public async Task<IActionResult> AcknowledgeIncident(int id, CancellationToken cancellationToken)
    {
        var incident = await _db.Incidents
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
            return NotFound();

        if (incident.Status == IncidentStatus.Closed)
            return BadRequest(new { message = "Closed incident cannot be acknowledged." });

        if (incident.Status != IncidentStatus.Acknowledged)
        {
            incident.Status = IncidentStatus.Acknowledged;

            await _db.SaveChangesAsync(cancellationToken);
            await _telegram.SendIncidentUpdatedAsync(incident, cancellationToken);
        }

        return Ok(incident);
    }

    [HttpPatch("{id:int}/close")]
    public async Task<IActionResult> CloseIncident(int id, CancellationToken cancellationToken)
    {
        var incident = await _db.Incidents
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (incident is null)
            return NotFound();

        if (incident.Status != IncidentStatus.Closed)
        {
            incident.Status = IncidentStatus.Closed;
            incident.ClosedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            await _telegram.SendIncidentUpdatedAsync(incident, cancellationToken);
        }

        return Ok(incident);
    }
}