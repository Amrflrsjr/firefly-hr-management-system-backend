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
    public async Task<ActionResult<IEnumerable<CashAdvance>>> GetCashAdvances()
    {
        return await _context.CashAdvances
            .Include(c => c.Employee)
            .ToListAsync();
    }

    // Remove [Authorize(Roles = "Admin")] so employees can submit requests
    [HttpPost]
    public async Task<ActionResult<CashAdvance>> PostCashAdvance(CashAdvance cashAdvance)
    {
        cashAdvance.Status = "Pending"; // Set default status to Pending for review
        cashAdvance.RemainingBalance = cashAdvance.CashAdvanceAmount;
        _context.CashAdvances.Add(cashAdvance);
        await _context.SaveChangesAsync();
        return Ok(cashAdvance);
    }

    // Admin approve endpoint
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/approve")]
    public async Task<IActionResult> ApproveCashAdvance(int id)
    {
        var ca = await _context.CashAdvances.FindAsync(id);
        if (ca == null) return NotFound();

        ca.Status = "Active"; // Set status to Active upon approval
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // Admin decline endpoint
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/decline")]
    public async Task<IActionResult> DeclineCashAdvance(int id)
    {
        var ca = await _context.CashAdvances.FindAsync(id);
        if (ca == null) return NotFound();

        ca.Status = "Declined";
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelCashAdvance(int id)
    {
        var ca = await _context.CashAdvances.FindAsync(id);
        if (ca == null) return NotFound();

        ca.Status = "Canceled";
        await _context.SaveChangesAsync();
        return NoContent();
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