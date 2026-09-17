using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FireflyHR.API.Data;
using FireflyHR.API.Models;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class LeavesController : ControllerBase
{
    private readonly AppDbContext _context;

    public LeavesController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetLeaves()
    {
        var leaves = await _context.Leaves
            .Include(l => l.Employee)
            .OrderByDescending(l => l.LeaveDate)
            .Select(l => new
            {
                l.Id,
                l.EmployeeId,
                EmployeeName = l.Employee != null ? $"{l.Employee.LastName}, {l.Employee.FirstName}" : "Unknown Employee",
                l.LeaveDate,
                l.LeaveHours,
                l.Status
            })
            .ToListAsync();

        return Ok(leaves);
    }

    [HttpGet("employee/{employeeId}")]
    public async Task<IActionResult> GetEmployeeLeaves(int employeeId)
    {
        var leaves = await _context.Leaves
            .Include(l => l.Employee)
            .Where(l => l.EmployeeId == employeeId)
            .OrderByDescending(l => l.LeaveDate)
            .Select(l => new
            {
                l.Id,
                l.EmployeeId,
                EmployeeName = l.Employee != null ? $"{l.Employee.LastName}, {l.Employee.FirstName}" : "Unknown Employee",
                l.LeaveDate,
                l.LeaveHours,
                l.Status
            })
            .ToListAsync();

        return Ok(leaves);
    }

    [HttpPost]
    public async Task<ActionResult<Leave>> PostLeave(Leave leave)
    {
        leave.Status = "In Review";
        _context.Leaves.Add(leave);
        await _context.SaveChangesAsync();

        // Fetch employee details for the notification message
        var employee = await _context.Employees.FindAsync(leave.EmployeeId);
        string empName = employee != null ? $"{employee.FirstName} {employee.LastName}" : "An employee";

        // Dynamically find ALL admin accounts from the database (No hardcoding)
        var adminAccounts = await _context.Employees
            .Where(e => e.IsAdmin == true)
            .ToListAsync();

        // Create a notification for every admin found
        foreach (var admin in adminAccounts)
        {
            _context.Notifications.Add(new Notification
            {
                EmployeeId = admin.Id,
                Title = "New Leave Request Pending",
                Message = $"{empName} filed a leave request for {leave.LeaveDate:MMM dd, yyyy}.",
                Type = "Leave" // <--- MUST be strictly "Leave"
            });
        }
        await _context.SaveChangesAsync();

        return Ok(leave);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateLeaveStatus(int id, [FromBody] string status)
    {
        var leave = await _context.Leaves.FindAsync(id);
        if (leave == null) return NotFound();
        leave.Status = status;
        await _context.SaveChangesAsync();

        // Notify the employee about their leave status change
        _context.Notifications.Add(new Notification
        {
            EmployeeId = leave.EmployeeId,
            Title = $"Leave Request {status}",
            Message = $"Your leave request for {leave.LeaveDate:MMM dd, yyyy} has been {status.ToLower()}.",
            Type = "Leave"
        });
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/reject")]
    public async Task<IActionResult> RejectLeave(int id)
    {
        var leave = await _context.Leaves.FindAsync(id);
        if (leave == null) return NotFound("Leave request not found.");

        leave.Status = "Declined";
        await _context.SaveChangesAsync();

        // Notify employee of rejection
        _context.Notifications.Add(new Notification
        {
            EmployeeId = leave.EmployeeId,
            Title = "Leave Request Declined",
            Message = $"Your leave request for {leave.LeaveDate:MMM dd, yyyy} has been declined.",
            Type = "Leave"
        });
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Leave request declined." });
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelLeave(int id)
    {
        var leave = await _context.Leaves.FindAsync(id);
        if (leave == null) return NotFound("Leave request not found.");

        if (leave.Status != "In Review")
        {
            return BadRequest("Only pending requests can be cancelled.");
        }

        leave.Status = "Cancelled";
        await _context.SaveChangesAsync();
        return Ok(new { Message = "Leave request cancelled." });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteLeave(int id)
    {
        var leave = await _context.Leaves.FindAsync(id);
        if (leave == null) return NotFound();
        _context.Leaves.Remove(leave);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}