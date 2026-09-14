using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class AttendanceRequestsController : ControllerBase
{
    private readonly AppDbContext _context;

    public AttendanceRequestsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateRequest([FromBody] AttendanceRequest request)
    {
        request.Status = "Pending";
        request.DateRequested = DateTime.UtcNow;
        _context.AttendanceRequests.Add(request);
        await _context.SaveChangesAsync();
        return Ok(request);
    }

    [HttpGet]
    public async Task<IActionResult> GetRequests() =>
        Ok(await _context.AttendanceRequests.Include(a => a.Employee).ToListAsync());

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/approve")]
    public async Task<IActionResult> ApproveRequest(int id)
    {
        var request = await _context.AttendanceRequests.FindAsync(id);
        if (request == null) return NotFound();

        request.Status = "Approved";

        // Automatically create the official TimeRecord upon approval
        _context.TimeRecords.Add(new TimeRecord
        {
            EmployeeId = request.EmployeeId,
            Type = request.Type,
            DateCreated = DateTime.SpecifyKind(request.TargetDate, DateTimeKind.Utc)
        });

        await _context.SaveChangesAsync();
        return Ok(new { Message = "Request approved and time record logged." });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRequest(int id)
    {
        var request = await _context.AttendanceRequests.FindAsync(id);
        if (request == null) return NotFound();

        _context.AttendanceRequests.Remove(request);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}