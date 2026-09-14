using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FireflyHR.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PayrollController : ControllerBase
{
    private readonly AppDbContext _context;

    public PayrollController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("calculate-params/{employeeId}")]
    public async Task<IActionResult> CalculatePayrollParams(int employeeId, [FromQuery] string payPeriod = "15th")
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound("Employee not found.");

        DateTime now = DateTime.UtcNow;
        DateTime startDate;
        DateTime endDate;

        // Cutoff Rule: 2 Days before cutoff (15th: prev month 30th - current 13th, 30th: 14th - 28th)
        if (payPeriod == "15th")
        {
            DateTime prevMonth = now.AddMonths(-1);
            int lastDayPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
            int startDay = Math.Min(30, lastDayPrevMonth);
            startDate = new DateTime(prevMonth.Year, prevMonth.Month, startDay, 0, 0, 0, DateTimeKind.Utc);
            endDate = new DateTime(now.Year, now.Month, 13, 23, 59, 59, DateTimeKind.Utc);
        }
        else
        {
            startDate = new DateTime(now.Year, now.Month, 14, 0, 0, 0, DateTimeKind.Utc);
            endDate = new DateTime(now.Year, now.Month, 28, 23, 59, 59, DateTimeKind.Utc);
        }

        // Load Holidays within cutoff period
        var periodHolidays = await _context.Holidays
            .Where(h => h.HolidayDate >= startDate && h.HolidayDate <= endDate)
            .ToListAsync();

        var holidayDates = periodHolidays.Select(h => h.HolidayDate.Date).ToHashSet();

        // 1. Time Records & Worked Days Calculation (Excluding holidays from regular days)
        var timeRecords = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId && t.DateCreated >= startDate && t.DateCreated <= endDate)
            .OrderBy(t => t.DateCreated)
            .ToListAsync();

        var groupedLogs = timeRecords.GroupBy(t => t.DateCreated.Date);

        // Rule: (Daily Rate + Daily Allowance) x No. of Days [don't include holidays]
        int daysWorked = groupedLogs.Count(g => !holidayDates.Contains(g.Key));

        decimal lateHoursTotal = 0;
        decimal undertimeHoursTotal = 0;
        decimal regularHolidayHoursTotal = 0;
        decimal specialNonWorkingHoursTotal = 0;

        foreach (var group in groupedLogs)
        {
            var dayDate = group.Key;
            var timeInUtc = group.FirstOrDefault(t => t.Type == "IN")?.DateCreated;
            var timeOutUtc = group.FirstOrDefault(t => t.Type == "OUT")?.DateCreated;

            var holiday = periodHolidays.FirstOrDefault(h => h.HolidayDate.Date == dayDate);

            if (timeInUtc.HasValue && timeOutUtc.HasValue)
            {
                decimal hoursWorkedOnDay = (decimal)(timeOutUtc.Value - timeInUtc.Value).TotalHours;

                if (holiday != null)
                {
                    if (holiday.HolidayType == "Regular Holiday")
                    {
                        regularHolidayHoursTotal += hoursWorkedOnDay;
                    }
                    else if (holiday.HolidayType == "Special Non-Working Holiday")
                    {
                        specialNonWorkingHoursTotal += hoursWorkedOnDay;
                    }
                }
            }

            // Only compute late/undertime for regular non-holiday days
            if (holiday == null && timeInUtc.HasValue && timeOutUtc.HasValue)
            {
                // Convert to Philippine Standard Time (+8) for shift comparisons
                DateTime timeInLocal = timeInUtc.Value.ToUniversalTime().AddHours(8);
                DateTime timeOutLocal = timeOutUtc.Value.ToUniversalTime().AddHours(8);

                if (employee.OfficeType == "Admin")
                {
                    // Fixed 9:00 AM - 6:00 PM with 5-min grace period (9:05 AM threshold)
                    DateTime expectedIn = timeInLocal.Date.AddHours(9);
                    DateTime graceThreshold = expectedIn.AddMinutes(5);

                    if (timeInLocal > graceThreshold)
                    {
                        lateHoursTotal += (decimal)(timeInLocal - expectedIn).TotalHours;
                    }

                    DateTime expectedOut = timeOutLocal.Date.AddHours(18); // 6:00 PM
                    if (timeOutLocal < expectedOut)
                    {
                        undertimeHoursTotal += (decimal)(expectedOut - timeOutLocal).TotalHours;
                    }
                }
                else if (employee.OfficeType == "Production")
                {
                    // Production: 8:00 AM - 10:00 AM Flex-in, 5:00 PM (17:00) expected Out
                    DateTime maxAllowedIn = timeInLocal.Date.AddHours(10);
                    if (timeInLocal > maxAllowedIn)
                    {
                        lateHoursTotal += (decimal)(timeInLocal - maxAllowedIn).TotalHours;
                    }

                    DateTime expectedOut = timeOutLocal.Date.AddHours(17); // 5:00 PM
                    if (timeOutLocal < expectedOut)
                    {
                        undertimeHoursTotal += (decimal)(expectedOut - timeOutLocal).TotalHours;
                    }
                }
            }
        }

        // 2. Approved Overtime Hours
        var overtimes = await _context.Overtimes
            .Where(o => o.EmployeeId == employeeId && o.Status == "Approved" && o.OvertimeDate >= startDate && o.OvertimeDate <= endDate)
            .ToListAsync();
        decimal overtimeHours = overtimes.Sum(o => o.OvertimeHours);

        // 3. Approved Leave Hours
        var leaves = await _context.Leaves
            .Where(l => l.EmployeeId == employeeId && l.Status == "Approved" && l.LeaveDate >= startDate && l.LeaveDate <= endDate)
            .ToListAsync();
        decimal approvedLeaveHours = leaves.Sum(l => l.LeaveHours);

        // 4. Active Cash Advances
        var cashAdvances = await _context.CashAdvances
            .Where(c => c.EmployeeId == employeeId && c.Status == "Active")
            .ToListAsync();

        decimal cashAdvanceDeduction = cashAdvances.Sum(c => c.CashAdvanceAmount) / (employee.DeductionType == "Per Pay Period" ? 2.0m : 1.0m);

        return Ok(new
        {
            daysWorked,
            overtimeHours,
            approvedLeaveHours,
            cashAdvanceDeduction,
            regularHolidayHours = Math.Round(regularHolidayHoursTotal, 2),
            specialNonWorkingHours = Math.Round(specialNonWorkingHoursTotal, 2),
            lateHours = Math.Round(lateHoursTotal, 2),
            undertimeHours = Math.Round(undertimeHoursTotal, 2),
            absentDays = 0
        });
    }

    [HttpGet("compute/{employeeId}")]
    public async Task<IActionResult> ComputePayroll(int employeeId, [FromQuery] CalculatePayrollParams queryParams)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound("Employee not found.");

        string periodName = queryParams.PayPeriod == "15th" ? "15th Pay Period" : "End of Month Pay Period";
        DateTime now = DateTime.UtcNow;

        // Strict Rule: Block creation if a pay slip already exists for this employee, period, and month
        bool exists = await _context.PaySlips.AnyAsync(p =>
            p.EmployeeId == employeeId &&
            p.PayPeriod == periodName &&
            p.PayPeriodEnd.Month == now.Month &&
            p.PayPeriodEnd.Year == now.Year);

        if (exists)
        {
            return BadRequest($"A {periodName} pay slip already exists for this month. Delete it from History first to recalculate.");
        }

        // 1. Calculate Earnings & Deductions
        decimal dailyAllowance = employee.MonthlyAllowance / 22.0m;
        decimal combinedDailyRate = employee.DailySalary + dailyAllowance;
        decimal hourlyRate = employee.DailySalary / 8.0m;

        decimal basicPay = combinedDailyRate * queryParams.DaysWorked;
        decimal overtimePay = queryParams.OvertimeHours * hourlyRate * 1.25m;
        decimal regularHolidayPay = queryParams.RegularHolidayHours * hourlyRate * 2.0m;
        decimal specialHolidayPay = queryParams.SpecialNonWorkingHours * hourlyRate * 1.3m;
        decimal leavePay = queryParams.ApprovedLeaveHours * hourlyRate;

        decimal grossEarnings = basicPay + overtimePay + regularHolidayPay + specialHolidayPay + leavePay;

        decimal lateDeduction = queryParams.LateHours * hourlyRate;
        decimal undertimeDeduction = queryParams.UndertimeHours * hourlyRate;
        decimal absentDeduction = queryParams.AbsentDays * combinedDailyRate;

        decimal govtContributions = employee.DeductionType == "Per Pay Period" ? 590.0m : 1180.0m;

        decimal totalDeductions = lateDeduction + undertimeDeduction + absentDeduction + queryParams.CashAdvanceDeduction + govtContributions;
        decimal netReceivable = grossEarnings - totalDeductions;

        var paySlip = new PaySlip
        {
            EmployeeId = employeeId,
            PayPeriod = periodName,
            PayPeriodEnd = now,
            DailySalary = employee.DailySalary,
            BasicPay = Math.Round(basicPay, 2),
            OvertimePay = Math.Round(overtimePay, 2),
            RegularHolidayPay = Math.Round(regularHolidayPay, 2),
            SpecialHolidayPay = Math.Round(specialHolidayPay, 2),
            LeavePay = Math.Round(leavePay, 2),
            GrossEarnings = Math.Round(grossEarnings, 2),
            LateDeduction = Math.Round(lateDeduction, 2),
            UndertimeDeduction = Math.Round(undertimeDeduction, 2),
            AbsentDeduction = Math.Round(absentDeduction, 2),
            CashAdvanceDeduction = Math.Round(queryParams.CashAdvanceDeduction, 2),
            GovernmentContributions = Math.Round(govtContributions, 2),
            TotalDeductions = Math.Round(totalDeductions, 2),
            NetReceivable = Math.Round(netReceivable, 2),
            DateCreated = now
        };

        _context.PaySlips.Add(paySlip);

        // Inside ComputePayroll method right after _context.PaySlips.Add(paySlip);

        if (queryParams.CashAdvanceDeduction > 0)
        {
            var activeAdvances = await _context.CashAdvances
                .Where(c => c.EmployeeId == employeeId && c.Status == "Active")
                .OrderBy(c => c.Date)
                .ToListAsync();

            decimal remainingDeductionToApply = queryParams.CashAdvanceDeduction;

            foreach (var advance in activeAdvances)
            {
                if (remainingDeductionToApply <= 0) break;

                decimal currentBalance = advance.RemainingBalance > 0 ? advance.RemainingBalance : advance.CashAdvanceAmount;
                decimal deductionForThisRecord = Math.Min(remainingDeductionToApply, currentBalance);

                advance.RemainingBalance = currentBalance - deductionForThisRecord;
                remainingDeductionToApply -= deductionForThisRecord;

                if (advance.RemainingBalance <= 0)
                {
                    advance.RemainingBalance = 0;
                    advance.Status = "Paid";
                }
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            employeeName = $"{employee.LastName}, {employee.FirstName}",
            dailySalary = employee.DailySalary,
            basicPay = Math.Round(basicPay, 2),
            overtimePay = Math.Round(overtimePay, 2),
            regularHolidayPay = Math.Round(regularHolidayPay, 2),
            specialHolidayPay = Math.Round(specialHolidayPay, 2),
            leavePay = Math.Round(leavePay, 2),
            grossEarnings = Math.Round(grossEarnings, 2),
            lateDeduction = Math.Round(lateDeduction, 2),
            undertimeDeduction = Math.Round(undertimeDeduction, 2),
            absentDeduction = Math.Round(absentDeduction, 2),
            cashAdvanceDeduction = Math.Round(queryParams.CashAdvanceDeduction, 2),
            governmentContributions = Math.Round(govtContributions, 2),
            totalDeductions = Math.Round(totalDeductions, 2),
            netReceivable = Math.Round(netReceivable, 2)
        });
    }



    [HttpGet("history/{employeeId}")]
    public async Task<IActionResult> GetPayrollHistory(int employeeId)
    {
        var history = await _context.PaySlips
            .Include(p => p.Employee)
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.DateCreated)
            .Select(p => new
            {
                p.Id,
                p.PayPeriod,
                p.PayPeriodEnd,
                EmployeeName = p.Employee != null ? $"{p.Employee.LastName}, {p.Employee.FirstName}" : "",
                DailySalary = p.DailySalary > 0 ? p.DailySalary : (p.Employee != null ? p.Employee.DailySalary : 0),
                p.BasicPay,
                p.OvertimePay,
                p.RegularHolidayPay,
                p.SpecialHolidayPay,
                p.LeavePay,
                p.GrossEarnings,
                p.LateDeduction,
                p.UndertimeDeduction,
                p.AbsentDeduction,
                p.CashAdvanceDeduction,
                p.GovernmentContributions,
                p.TotalDeductions,
                p.NetReceivable
            })
            .ToListAsync();

        return Ok(history);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePaySlip(int id)
    {
        var paySlip = await _context.PaySlips.FindAsync(id);
        if (paySlip == null) return NotFound("Pay slip record not found.");

        // 1. If a cash advance deduction was part of this payslip, restore balance and status
        if (paySlip.CashAdvanceDeduction > 0)
        {
            var employeeAdvances = await _context.CashAdvances
                .Where(c => c.EmployeeId == paySlip.EmployeeId)
                .OrderByDescending(c => c.Date)
                .ToListAsync();

            decimal amountToRestore = paySlip.CashAdvanceDeduction;

            foreach (var advance in employeeAdvances)
            {
                if (amountToRestore <= 0) break;

                // Calculate how much can be restored to this specific advance
                decimal maxRestorable = advance.CashAdvanceAmount - advance.RemainingBalance;
                decimal restoreChunk = Math.Min(amountToRestore, maxRestorable);

                // If maxRestorable was 0 (e.g. legacy records without RemainingBalance initialized), restore fully
                if (maxRestorable == 0 && advance.RemainingBalance == 0)
                {
                    restoreChunk = Math.Min(amountToRestore, advance.CashAdvanceAmount);
                }

                advance.RemainingBalance += restoreChunk;
                amountToRestore -= restoreChunk;

                // Re-open status to Active if balance is restored
                if (advance.RemainingBalance > 0)
                {
                    advance.Status = "Active";
                }
            }
        }

        // 2. Delete the pay slip
        _context.PaySlips.Remove(paySlip);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Pay slip record deleted successfully and cash advance balance restored." });
    }
}