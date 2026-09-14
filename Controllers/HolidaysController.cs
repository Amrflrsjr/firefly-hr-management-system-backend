using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FireflyHR.API.Data;
using FireflyHR.API.Models;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class HolidaysController : ControllerBase
{
    private readonly AppDbContext _context;

    public HolidaysController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Holiday>>> GetHolidays() => await _context.Holidays.ToListAsync();

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Holiday>> PostHoliday(Holiday holiday)
    {
        _context.Holidays.Add(holiday);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetHolidays), new { id = holiday.Id }, holiday);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteHoliday(int id)
    {
        var holiday = await _context.Holidays.FindAsync(id);
        if (holiday == null) return NotFound();
        _context.Holidays.Remove(holiday);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateHoliday(int id, Holiday holiday)
    {
        if (id != holiday.Id) return BadRequest();
        _context.Entry(holiday).State = EntityState.Modified;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.Holidays.Any(e => e.Id == id)) return NotFound();
            throw;
        }
        return NoContent();
    }
}