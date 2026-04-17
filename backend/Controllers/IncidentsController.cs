using backend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Services;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IncidentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IncidentService _incidentService;

    public IncidentsController(AppDbContext db, IncidentService incidentService)
    {
        _db = db;
        _incidentService = incidentService;
    }

    [HttpGet]
    public async Task<IActionResult> GetIncidents([FromQuery] int limit = 20)
    {
        var incidents = await _db.Incidents
            .Include(i => i.IncidentEventLinks)
            .ThenInclude(link => link.SecurityEvent)
            .OrderByDescending(i => i.Id)
            .Take(limit)
            .ToListAsync();

        return Ok(incidents);
    }

    [HttpPatch("{id:int}/ack")]
    public async Task<IActionResult> AcknowledgeIncident(int id, CancellationToken cancellationToken)
    {
        try
        {
            var incident = await _incidentService.AcknowledgeIncidentAsync(id, cancellationToken);

            if (incident is null)
                return NotFound();

            return Ok(incident);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:int}/close")]
    public async Task<IActionResult> CloseIncident(int id, CancellationToken cancellationToken)
    {
        var incident = await _incidentService.CloseIncidentAsync(id, cancellationToken);

        if (incident is null)
            return NotFound();

        return Ok(incident);
    }
}