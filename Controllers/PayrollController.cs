using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FireflyHR.API.Controllers;

[Authorize]
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

        // 1. Calculate Earnings & Deductions using Daily Allowance
        decimal dailyAllowance = employee.DailyAllowance;
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

        // 2. Dynamic Statutory Government Contribution Calculations
        decimal sssDeduction = 0;
        decimal philHealthDeduction = 0;
        decimal pagIbigDeduction = 0;

        if (employee.HasGovernmentDeductions)
        {
            bool isPerPeriod = employee.DeductionType == "Per Pay Period";

            // Estimated Monthly Basic Income based on 26 standard working days
            decimal estimatedMonthlySalary = employee.DailySalary * 26.0m;

            // A. Pag-IBIG: 2% of salary up to Max Salary Ceiling (₱10,000 cap = ₱200/mo, ₱100/cutoff)
            decimal monthlyPagIbig = Math.Min(estimatedMonthlySalary * 0.02m, 200.0m);
            pagIbigDeduction = isPerPeriod ? (monthlyPagIbig / 2.0m) : monthlyPagIbig;

            // B. PhilHealth: 5% total rate split between employer & employee (2.5% employee share)
            // Min floor: ₱10,000 monthly (₱250 EE share / ₱125 per cutoff), Max cap: ₱100,000 monthly
            decimal boundedPhilHealthBase = Math.Clamp(estimatedMonthlySalary, 10000.0m, 100000.0m);
            decimal monthlyPhilHealth = boundedPhilHealthBase * 0.025m;
            philHealthDeduction = isPerPeriod ? (monthlyPhilHealth / 2.0m) : monthlyPhilHealth;

            // C. SSS: Dynamic MSC Bracket Math (~4.5% Employee Share cap at ₱30,000 MSC)
            // For ₱680/day (₱17,680 monthly), MSC = ₱16,000 -> ₱720.00 semi-monthly deduction
            decimal monthlySss = CalculateSssEmployeeContribution(estimatedMonthlySalary);
            sssDeduction = isPerPeriod ? (monthlySss / 2.0m) : monthlySss;
        }

        decimal totalGovtContributions = sssDeduction + philHealthDeduction + pagIbigDeduction;

        decimal totalDeductions = lateDeduction + undertimeDeduction + absentDeduction + queryParams.CashAdvanceDeduction + totalGovtContributions;
        decimal netReceivable = grossEarnings - totalDeductions;

        var paySlip = new PaySlip
        {
            EmployeeId = employeeId,
            PayPeriod = periodName,
            PayPeriodEnd = now,
            DailySalary = employee.DailySalary,
            DailyAllowance = dailyAllowance,
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

            // Government Breakdown fields
            SssDeduction = Math.Round(sssDeduction, 2),
            PhilHealthDeduction = Math.Round(philHealthDeduction, 2),
            PagIbigDeduction = Math.Round(pagIbigDeduction, 2),
            GovernmentContributions = Math.Round(totalGovtContributions, 2),

            TotalDeductions = Math.Round(totalDeductions, 2),
            NetReceivable = Math.Round(netReceivable, 2),
            DateCreated = now
        };

        _context.PaySlips.Add(paySlip);

        // Handle Cash Advance Balance Reductions
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
            dailyAllowance = dailyAllowance,
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

            // Return individual deduction components to client UI
            sssDeduction = Math.Round(sssDeduction, 2),
            philHealthDeduction = Math.Round(philHealthDeduction, 2),
            pagIbigDeduction = Math.Round(pagIbigDeduction, 2),
            governmentContributions = Math.Round(totalGovtContributions, 2),

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
                DailyAllowance = p.DailyAllowance > 0 ? p.DailyAllowance : (p.Employee != null ? p.Employee.DailyAllowance : 0),
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

                p.SssDeduction,
                p.PhilHealthDeduction,
                p.PagIbigDeduction,
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

                decimal maxRestorable = advance.CashAdvanceAmount - advance.RemainingBalance;
                decimal restoreChunk = Math.Min(amountToRestore, maxRestorable);

                if (maxRestorable == 0 && advance.RemainingBalance == 0)
                {
                    restoreChunk = Math.Min(amountToRestore, advance.CashAdvanceAmount);
                }

                advance.RemainingBalance += restoreChunk;
                amountToRestore -= restoreChunk;

                if (advance.RemainingBalance > 0)
                {
                    advance.Status = "Active";
                }
            }
        }

        _context.PaySlips.Remove(paySlip);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Pay slip record deleted successfully and cash advance balance restored." });
    }

    // Helper: Compute Monthly SSS Employee Contribution based on MSC Brackets
    private static decimal CalculateSssEmployeeContribution(decimal monthlySalary)
    {
        if (monthlySalary <= 4250.0m) return 400.0m;
        if (monthlySalary >= 29750.0m) return 2700.0m; // 30,000 MSC cap

        // Step by 500 increments on MSC brackets (4.5% Employee share)
        decimal msc = Math.Floor((monthlySalary - 4250.0m) / 500.0m) * 500.0m + 4500.0m;
        return msc * 0.045m;
    }
}