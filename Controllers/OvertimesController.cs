using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FireflyHR.API.Data;
using FireflyHR.API.Models;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class OvertimesController : ControllerBase
{
    private readonly AppDbContext _context;

    public OvertimesController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetOvertimes()
    {
        var overtimes = await _context.Overtimes
            .Include(o => o.Employee)
            .OrderByDescending(o => o.OvertimeDate)
            .Select(o => new
            {
                o.Id,
                o.EmployeeId,
                EmployeeName = o.Employee != null ? $"{o.Employee.LastName}, {o.Employee.FirstName}" : "Unknown Employee",
                o.OvertimeDate,
                o.OvertimeHours,
                o.Status
            })
            .ToListAsync();

        return Ok(overtimes);
    }

    [HttpGet("employee/{employeeId}")]
    public async Task<IActionResult> GetEmployeeOvertimes(int employeeId)
    {
        var overtimes = await _context.Overtimes
            .Include(o => o.Employee)
            .Where(o => o.EmployeeId == employeeId) // Do not filter out status here!
            .OrderByDescending(o => o.OvertimeDate)
            .Select(o => new
            {
                o.Id,
                o.EmployeeId,
                EmployeeName = o.Employee != null ? $"{o.Employee.LastName}, {o.Employee.FirstName}" : "Unknown Employee",
                o.OvertimeDate,
                o.OvertimeHours,
                o.Status
            })
            .ToListAsync();

        return Ok(overtimes);
    }

    [HttpPost]
    public async Task<ActionResult<Overtime>> PostOvertime(Overtime overtime)
    {
        overtime.Status = "In Review";
        _context.Overtimes.Add(overtime);
        await _context.SaveChangesAsync();

        var employee = await _context.Employees.FindAsync(overtime.EmployeeId);
        string empName = employee != null ? $"{employee.FirstName} {employee.LastName}" : "An employee";

        // Dynamically fetch ALL accounts where IsAdmin is true (No hardcoded IDs)
        var admins = await _context.Employees
            .Where(e => e.IsAdmin == true)
            .ToListAsync();

        // Loop through all valid admins found in the database and create a notification mapped strictly to Overtime type
        foreach (var admin in admins)
        {
            _context.Notifications.Add(new Notification
            {
                EmployeeId = admin.Id,
                Title = "New Overtime Request Pending",
                Message = $"{empName} filed an overtime request for {overtime.OvertimeDate:MMM dd, yyyy}.",
                Type = "Overtime" // <--- Mapped strictly to Overtime tab
            });
        }
        await _context.SaveChangesAsync();

        return Ok(overtime);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateOvertimeStatus(int id, [FromBody] string status)
    {
        var ot = await _context.Overtimes.FindAsync(id);
        if (ot == null) return NotFound();
        ot.Status = status;
        await _context.SaveChangesAsync();

        // Notify employee
        _context.Notifications.Add(new Notification
        {
            EmployeeId = ot.EmployeeId,
            Title = $"Overtime Request {status}",
            Message = $"Your overtime request for {ot.OvertimeDate:MMM dd, yyyy} has been {status.ToLower()}.",
            Type = "Overtime"
        });
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/reject")]
    public async Task<IActionResult> RejectOvertime(int id)
    {
        var ot = await _context.Overtimes.FindAsync(id);
        if (ot == null) return NotFound("Overtime record not found.");

        ot.Status = "Declined";
        await _context.SaveChangesAsync();

        // Notify employee
        _context.Notifications.Add(new Notification
        {
            EmployeeId = ot.EmployeeId,
            Title = "Overtime Request Declined",
            Message = $"Your overtime request for {ot.OvertimeDate:MMM dd, yyyy} has been declined.",
            Type = "Overtime"
        });
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Overtime request declined." });
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelOvertime(int id)
    {
        var ot = await _context.Overtimes.FindAsync(id);
        if (ot == null) return NotFound("Overtime record not found.");

        if (ot.Status != "In Review")
        {
            return BadRequest("Only pending overtime requests can be cancelled.");
        }

        ot.Status = "Cancelled";
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Overtime request cancelled." });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteOvertime(int id)
    {
        var overtime = await _context.Overtimes.FindAsync(id);
        if (overtime == null) return NotFound();
        _context.Overtimes.Remove(overtime);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}