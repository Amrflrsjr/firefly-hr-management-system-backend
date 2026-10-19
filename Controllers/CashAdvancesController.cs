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

    // Helper: Get the current cutoff window (same logic as PayrollController)
    private static (DateTime StartUtc, DateTime EndUtc) GetPayPeriodWindow(DateTime nowUtc)
    {
        DateTime nowPht = nowUtc.AddHours(8);
        DateTime startPht, endPht;

        // Determine current cutoff based on today's date
        if (nowPht.Day <= 13)
        {
            // 15th cutoff of the current month
            DateTime prevMonth = nowPht.AddMonths(-1);
            int lastDayPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
            int secondToLastDay = lastDayPrevMonth - 1;

            startPht = new DateTime(prevMonth.Year, prevMonth.Month, secondToLastDay, 0, 0, 0);
            endPht = new DateTime(nowPht.Year, nowPht.Month, 13, 23, 59, 59);
        }
        else if (nowPht.Day >= 14 && nowPht.Day <= 28)
        {
            // End of Month cutoff
            startPht = new DateTime(nowPht.Year, nowPht.Month, 14, 0, 0, 0);
            endPht = new DateTime(nowPht.Year, nowPht.Month, 28, 23, 59, 59);
        }
        else
        {
            // Days 29, 30, 31 fall into the NEXT month's 15th cutoff
            DateTime nextMonth = nowPht.AddMonths(1);
            int lastDayCurrentMonth = DateTime.DaysInMonth(nowPht.Year, nowPht.Month);
            int secondToLastDay = lastDayCurrentMonth - 1;

            startPht = new DateTime(nowPht.Year, nowPht.Month, secondToLastDay, 0, 0, 0);
            endPht = new DateTime(nextMonth.Year, nextMonth.Month, 13, 23, 59, 59);
        }

        DateTime startUtc = DateTime.SpecifyKind(startPht.AddHours(-8), DateTimeKind.Utc);
        DateTime endUtc = DateTime.SpecifyKind(endPht.AddHours(-8), DateTimeKind.Utc);

        return (startUtc, endUtc);
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CashAdvance>>> GetCashAdvances([FromQuery] string? type = null)
    {
        var query = _context.CashAdvances
            .Include(c => c.Employee)
            .AsQueryable();

        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(c => c.Type == type);
        }

        return await query
            .OrderByDescending(c => c.Id)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<CashAdvance>> PostCashAdvance(CashAdvance cashAdvance)
    {
        var employee = await _context.Employees.FindAsync(cashAdvance.EmployeeId);
        if (employee == null) return NotFound("Employee not found.");

        // Ensure Type is set
        cashAdvance.Type = string.IsNullOrEmpty(cashAdvance.Type) ? "Cash" : cashAdvance.Type;

        // --- NEW: SALARY ADVANCE LIMIT CHECK ---
        if (cashAdvance.Type == "Salary")
        {
            var (startDate, endDate) = GetPayPeriodWindow(DateTime.UtcNow);

            // 1. Calculate Days Worked in the current cutoff
            var timeRecords = await _context.TimeRecords
                .Where(t => t.EmployeeId == cashAdvance.EmployeeId && t.DateCreated >= startDate && t.DateCreated <= endDate)
                .ToListAsync();
            int daysWorked = timeRecords.GroupBy(t => t.DateCreated.AddHours(8).Date).Count();

            // 2. Calculate Gross Pay for this cutoff
            decimal actualDailyRate = employee.DailySalary + employee.DailyAllowance;
            decimal grossEarned = daysWorked * actualDailyRate;

            // 3. Calculate Existing Active Salary Advances (Pending or Active)
            var existingSalaryAdvances = await _context.CashAdvances
                .Where(c => c.EmployeeId == cashAdvance.EmployeeId &&
                            c.Type == "Salary" &&
                            (c.Status == "Active" || c.Status == "Pending"))
                .SumAsync(c => c.RemainingBalance > 0 ? c.RemainingBalance : c.CashAdvanceAmount);

            // 4. Check if requested amount exceeds remaining allowed
            decimal allowedLimit = grossEarned - existingSalaryAdvances;

            if (allowedLimit < 0) allowedLimit = 0; // Prevent negative limit

            if (cashAdvance.CashAdvanceAmount > allowedLimit)
            {
                return BadRequest($"Requested amount (₱{cashAdvance.CashAdvanceAmount:N2}) exceeds allowed salary advance. " +
                                  $"Based on {daysWorked} days worked (₱{grossEarned:N2} gross), " +
                                  $"and existing advances (₱{existingSalaryAdvances:N2}), you can only request up to ₱{allowedLimit:N2}.");
            }
        }
        // ---------------------------------------

        cashAdvance.Status = "Pending";
        cashAdvance.RemainingBalance = cashAdvance.CashAdvanceAmount;
        _context.CashAdvances.Add(cashAdvance);
        await _context.SaveChangesAsync();

        string empName = $"{employee.FirstName} {employee.LastName}";
        string advanceName = cashAdvance.Type == "Salary" ? "salary advance" : "cash advance";

        var admins = await _context.Employees
            .Where(e => e.IsAdmin == true)
            .ToListAsync();

        foreach (var admin in admins)
        {
            _context.Notifications.Add(new Notification
            {
                EmployeeId = admin.Id,
                Title = $"New {cashAdvance.Type} Advance Request Pending",
                Message = $"{empName} submitted a {advanceName} request for ₱{cashAdvance.CashAdvanceAmount:N2} requiring review.",
                Type = "Advance"
            });
        }
        await _context.SaveChangesAsync();

        return Ok(cashAdvance);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/approve")]
    public async Task<IActionResult> ApproveCashAdvance(int id)
    {
        var ca = await _context.CashAdvances.FindAsync(id);
        if (ca == null) return NotFound();

        ca.Status = "Active";
        await _context.SaveChangesAsync();

        _context.Notifications.Add(new Notification
        {
            EmployeeId = ca.EmployeeId,
            Title = $"{ca.Type} Advance Approved",
            Message = $"Your {ca.Type.ToLower()} advance request for ₱{ca.CashAdvanceAmount:N2} has been approved.",
            Type = "Advance"
        });
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}/decline")]
    public async Task<IActionResult> DeclineCashAdvance(int id)
    {
        var ca = await _context.CashAdvances.FindAsync(id);
        if (ca == null) return NotFound();

        ca.Status = "Declined";
        await _context.SaveChangesAsync();

        _context.Notifications.Add(new Notification
        {
            EmployeeId = ca.EmployeeId,
            Title = $"{ca.Type} Advance Declined",
            Message = $"Your {ca.Type.ToLower()} advance request for ₱{ca.CashAdvanceAmount:N2} has been declined.",
            Type = "Advance"
        });
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