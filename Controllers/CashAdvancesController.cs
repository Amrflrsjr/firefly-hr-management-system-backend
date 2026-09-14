using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FireflyHR.API.Data;
using FireflyHR.API.Models;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class CashAdvancesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CashAdvancesController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CashAdvance>>> GetCashAdvances() => await _context.CashAdvances.Include(c => c.Employee).ToListAsync();

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<CashAdvance>> PostCashAdvance(CashAdvance cashAdvance)
    {
        cashAdvance.Status = "Active";
        cashAdvance.RemainingBalance = cashAdvance.CashAdvanceAmount;
        _context.CashAdvances.Add(cashAdvance);
        await _context.SaveChangesAsync();
        return Ok(cashAdvance);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCashAdvance(int id, CashAdvance cashAdvance)
    {
        if (id != cashAdvance.Id) return BadRequest();
        _context.Entry(cashAdvance).State = EntityState.Modified;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.CashAdvances.Any(e => e.Id == id)) return NotFound();
            throw;
        }
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCashAdvance(int id)
    {
        var cashAdvance = await _context.CashAdvances.FindAsync(id);
        if (cashAdvance == null) return NotFound();
        _context.CashAdvances.Remove(cashAdvance);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}