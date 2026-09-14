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
        return NoContent();
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