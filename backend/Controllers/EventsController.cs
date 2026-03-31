using backend.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _db;

    public EventsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? deviceId,
        [FromQuery] string? sensor,
        [FromQuery(Name = "event")] string? eventName,
        [FromQuery] int limit = 20)
    {
        var query = _db.SecurityEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query = query.Where(e => e.DeviceId == deviceId);
        }

        if (!string.IsNullOrWhiteSpace(sensor))
        {
            query = query.Where(e => e.Sensor == sensor);
        }

        if (!string.IsNullOrWhiteSpace(eventName))
        {
            query = query.Where(e => e.Event == eventName);
        }

        var events = await query
            .OrderByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync();

        return Ok(events);
    }
}