using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;

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

    // Helper method to safely convert UTC to Philippine Standard Time across Windows and Linux (AWS)
    private static DateTime GetPstTime(DateTime utcDateTime)
    {
        TimeZoneInfo pstZone = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, pstZone);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRequest([FromBody] AttendanceRequest request)
    {
        var employee = await _context.Employees.FindAsync(request.EmployeeId);
        if (employee == null) return NotFound("Employee not found.");

        DateTime targetUtc = DateTime.SpecifyKind(request.TargetDate, DateTimeKind.Utc);
        DateTime targetPstDate = GetPstTime(targetUtc).Date;

        var existingLogs = await _context.TimeRecords
            .Where(t => t.EmployeeId == request.EmployeeId)
            .ToListAsync();

        // Duplicate check
        if (existingLogs.Any(t => GetPstTime(t.DateCreated).Date == targetPstDate && t.Type.Equals(request.Type, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest($"A Time {request.Type} record already exists for {targetPstDate:MMM dd, yyyy}.");
        }

        // Chronological Order Validation
        var existingIn = existingLogs.FirstOrDefault(t => t.Type == "IN" && GetPstTime(t.DateCreated).Date == targetPstDate);
        var existingOut = existingLogs.FirstOrDefault(t => t.Type == "OUT" && GetPstTime(t.DateCreated).Date == targetPstDate);

        if (request.Type == "OUT" && existingIn != null && targetUtc <= existingIn.DateCreated)
        {
            return BadRequest($"Time OUT must be later than Time IN ({GetPstTime(existingIn.DateCreated):hh:mm tt}).");
        }

        if (request.Type == "IN" && existingOut != null && targetUtc >= existingOut.DateCreated)
        {
            return BadRequest($"Time IN must be earlier than Time OUT ({GetPstTime(existingOut.DateCreated):hh:mm tt}).");
        }

        request.Status = "Pending";
        request.DateRequested = DateTime.UtcNow;
        request.TargetDate = targetUtc;

        _context.AttendanceRequests.Add(request);
        await _context.SaveChangesAsync();

        // --- DISPATCH NOTIFICATION TO ALL ADMINS ---
        string empName = employee != null ? $"{employee.FirstName} {employee.LastName}" : "An employee";
        var adminAccounts = await _context.Employees
            .Where(e => e.IsAdmin == true)
            .ToListAsync();

        foreach (var admin in adminAccounts)
        {
            _context.Notifications.Add(new Notification
            {
                EmployeeId = admin.Id,
                Title = "New Attendance Request Pending",
                Message = $"{empName} filed a correction request for Time {request.Type} on {targetPstDate:MMM dd, yyyy}.",
                Type = "Attendance" // <--- Notification Type category
            });
        }
        await _context.SaveChangesAsync();

        return Ok(request);
    }

    [HttpGet]
    public async Task<IActionResult> GetRequests() =>
        Ok(await _context.AttendanceRequests
            .Include(a => a.Employee)
            .OrderByDescending(a => a.DateRequested)
            .ToListAsync());

    [HttpGet("employee/{employeeId}")]
    public async Task<IActionResult> GetEmployeeRequests(int employeeId)
    {
        var requests = await _context.AttendanceRequests
            .Include(a => a.Employee)
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.DateRequested)
            .ToListAsync();

        return Ok(requests);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/approve")]
    public async Task<IActionResult> ApproveRequest(int id)
    {
        var request = await _context.AttendanceRequests
            .Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (request == null) return NotFound("Attendance request not found.");

        // Ensure Kind is explicitly Utc
        DateTime targetUtc = DateTime.SpecifyKind(request.TargetDate, DateTimeKind.Utc);
        DateTime targetPstDate = DateTime.SpecifyKind(GetPstTime(targetUtc).Date, DateTimeKind.Utc);

        // Verify no record was created between submission and approval
        var existingLogs = await _context.TimeRecords
            .Where(t => t.EmployeeId == request.EmployeeId)
            .ToListAsync();

        bool hasExistingLog = existingLogs.Any(t =>
            GetPstTime(t.DateCreated).Date == targetPstDate &&
            t.Type.Equals(request.Type, StringComparison.OrdinalIgnoreCase));

        if (hasExistingLog)
        {
            return BadRequest($"Cannot approve request. A Time {request.Type} record already exists for this date.");
        }

        request.Status = "Approved";

        // Automatically create the official TimeRecord with explicit UTC kinds and request flag
        _context.TimeRecords.Add(new TimeRecord
        {
            EmployeeId = request.EmployeeId,
            Type = request.Type,
            DateCreated = targetUtc,
            Date = targetPstDate,
            IsRequested = true
        });

        // --- DISPATCH NOTIFICATION TO EMPLOYEE ---
        _context.Notifications.Add(new Notification
        {
            EmployeeId = request.EmployeeId,
            Title = "Attendance Request Approved",
            Message = $"Your request for Time {request.Type} on {targetPstDate:MMM dd, yyyy} has been approved.",
            Type = "Attendance"
        });

        await _context.SaveChangesAsync();
        return Ok(new { Message = "Request approved and time record logged successfully." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/reject")]
    public async Task<IActionResult> RejectRequest(int id)
    {
        var request = await _context.AttendanceRequests
            .Include(a => a.Employee)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (request == null) return NotFound("Request not found.");

        request.Status = "Declined";

        // --- DISPATCH NOTIFICATION TO EMPLOYEE ---
        DateTime targetPstDate = GetPstTime(DateTime.SpecifyKind(request.TargetDate, DateTimeKind.Utc)).Date;
        _context.Notifications.Add(new Notification
        {
            EmployeeId = request.EmployeeId,
            Title = "Attendance Request Declined",
            Message = $"Your request for Time {request.Type} on {targetPstDate:MMM dd, yyyy} has been declined.",
            Type = "Attendance"
        });

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Request has been declined." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRequest(int id)
    {
        var request = await _context.AttendanceRequests.FindAsync(id);
        if (request == null) return NotFound("Request not found.");

        _context.AttendanceRequests.Remove(request);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}