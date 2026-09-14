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
        return NoContent();
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