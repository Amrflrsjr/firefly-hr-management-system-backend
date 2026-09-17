using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/Notifications/unread-count/{employeeId}?type=Leave
    [HttpGet("unread-count/{employeeId}")]
    public async Task<ActionResult<int>> GetUnreadCount(int employeeId, [FromQuery] string? type = null)
    {
        var query = _context.Notifications
            .Where(n => n.EmployeeId == employeeId && !n.IsRead);

        if (!string.IsNullOrEmpty(type))
        {
            // Strict case-insensitive matching against the notification Type column[cite: 4]
            query = query.Where(n => n.Type != null && n.Type.ToLower() == type.ToLower());
        }

        int count = await query.CountAsync();
        return Ok(count);
    }

    // GET: api/Notifications/{employeeId}
    [HttpGet("{employeeId}")]
    public async Task<ActionResult<IEnumerable<Notification>>> GetNotifications(int employeeId)
    {
        var notes = await _context.Notifications
            .Where(n => n.EmployeeId == employeeId)
            .OrderByDescending(n => n.DateCreated)
            .Take(20)
            .ToListAsync();

        return Ok(notes);
    }

    // PUT: api/Notifications/mark-type-read/{employeeId}?type=Leave
    [HttpPut("mark-type-read/{employeeId}")]
    public async Task<IActionResult> MarkTypeAsRead(int employeeId, [FromQuery] string type)
    {
        var notifications = await _context.Notifications
            .Where(n => n.EmployeeId == employeeId && !n.IsRead && n.Type != null && n.Type.ToLower() == type.ToLower())
            .ToListAsync();

        if (!notifications.Any()) return NoContent();

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // PUT: api/Notifications/mark-all-read/{employeeId}
    [HttpPut("mark-all-read/{employeeId}")]
    public async Task<IActionResult> MarkAllAsRead(int employeeId)
    {
        var unreadNotifications = await _context.Notifications
            .Where(n => n.EmployeeId == employeeId && !n.IsRead)
            .ToListAsync();

        if (!unreadNotifications.Any()) return NoContent();

        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }
}