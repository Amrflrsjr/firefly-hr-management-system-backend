using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FireflyHR.API.Data;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class ThirteenthMonthController : ControllerBase
{
    private readonly AppDbContext _context;

    public ThirteenthMonthController(AppDbContext context) => _context = context;

    [HttpGet("compute/{employeeId}")]
    public async Task<IActionResult> ComputeThirteenthMonth(int employeeId, decimal totalAnnualBasicIncome)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound("Employee not found.");

        // 13th month pay is total basic annual income divided by 12
        decimal amountReceivable = totalAnnualBasicIncome / 12.0m;

        return Ok(new
        {
            EmployeeName = $"{employee.LastName}, {employee.FirstName}",
            DateStarted = employee.DateHired.ToString("yyyy-MM-dd"),
            TotalAnnualIncomeAsOfDate = totalAnnualBasicIncome,
            AmountReceivableAsOfDate = amountReceivable
        });
    }
}