using backend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IncidentsController : ControllerBase
{
    private readonly AppDbContext _db;

    public IncidentsController(AppDbContext db)
    {
        _db = db;
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
}