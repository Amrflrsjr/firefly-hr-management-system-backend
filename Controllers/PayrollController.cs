using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

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
            // Strict cutoff at 13th 23:59:59 PHT (which is 15:59:59 UTC)
            endDate = new DateTime(now.Year, now.Month, 13, 15, 59, 59, DateTimeKind.Utc);
        }
        else
        {
            startDate = new DateTime(now.Year, now.Month, 14, 0, 0, 0, DateTimeKind.Utc);
            // Strict cutoff at 28th 23:59:59 PHT (which is 15:59:59 UTC) to prevent trailing records from leaking in
            endDate = new DateTime(now.Year, now.Month, 28, 15, 59, 59, DateTimeKind.Utc);
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
            var holiday = periodHolidays.FirstOrDefault(h => h.HolidayDate.Date == dayDate);

            // Get all IN and OUT logs for this day, ordered chronologically
            var dayLogs = group.OrderBy(t => t.DateCreated).ToList();

            var firstIn = dayLogs.FirstOrDefault(t => t.Type != null &&
                (t.Type.Equals("IN", StringComparison.OrdinalIgnoreCase) || t.Type.Equals("Time IN", StringComparison.OrdinalIgnoreCase)));

            var lastOut = dayLogs.LastOrDefault(t => t.Type != null &&
                (t.Type.Equals("OUT", StringComparison.OrdinalIgnoreCase) || t.Type.Equals("Time OUT", StringComparison.OrdinalIgnoreCase)));

            var timeInUtc = firstIn?.DateCreated;
            var timeOutUtc = lastOut?.DateCreated;

            if (timeInUtc.HasValue && timeOutUtc.HasValue && holiday != null)
            {
                decimal hoursWorkedOnDay = (decimal)(timeOutUtc.Value - timeInUtc.Value).TotalHours;
                decimal cappedHolidayHours = Math.Min(hoursWorkedOnDay, 8.0m);

                if (holiday.HolidayType == "Regular Holiday")
                {
                    regularHolidayHoursTotal += cappedHolidayHours;
                }
                else if (holiday.HolidayType == "Special Non-Working Holiday")
                {
                    specialNonWorkingHoursTotal += cappedHolidayHours;
                }
            }

            // Strict Day-by-Day Evaluation for Regular Non-Holiday Days
            if (holiday == null && timeInUtc.HasValue && timeOutUtc.HasValue)
            {
                // Convert to Philippine Standard Time (+8) for shift comparisons
                DateTime timeInLocal = timeInUtc.Value.ToUniversalTime().AddHours(8);
                DateTime timeOutLocal = timeOutUtc.Value.ToUniversalTime().AddHours(8);

                DateTime effectiveTimeIn = timeInLocal;
                decimal netHoursWorked = 0;

                if (employee.OfficeType == "Admin")
                {
                    // Fixed 9:00 AM - 6:00 PM with grace period allowing up to 9:05:59 AM
                    DateTime expectedIn = timeInLocal.Date.AddHours(9);
                    DateTime graceLimit = expectedIn.AddMinutes(5).AddSeconds(59);
                    DateTime standardShiftEnd = timeInLocal.Date.AddHours(18);

                    // 1. Independent Late Calculation
                    if (timeInLocal > graceLimit)
                    {
                        double lateMinutes = Math.Round((timeInLocal - expectedIn).TotalMinutes);
                        lateHoursTotal += (decimal)(lateMinutes / 60.0);
                    }
                    else if (timeInLocal > expectedIn && timeInLocal <= graceLimit)
                    {
                        effectiveTimeIn = expectedIn;
                    }

                    // 2. Independent Undertime Calculation (Check gap from 6:00 PM if left early)
                    if (timeOutLocal < standardShiftEnd)
                    {
                        double undertimeMinutes = Math.Round((standardShiftEnd - timeOutLocal).TotalMinutes);
                        undertimeHoursTotal += (decimal)(undertimeMinutes / 60.0);
                    }

                    // Net hours check for standard attendance count
                    DateTime expectedOut = timeInLocal.Date.AddHours(18);
                    if (timeOutLocal >= expectedOut)
                    {
                        netHoursWorked = 8.0m;
                    }
                    else
                    {
                        double elapsedMinutes = Math.Round((timeOutLocal - effectiveTimeIn).TotalMinutes);
                        decimal totalElapsedHours = (decimal)(elapsedMinutes / 60.0);
                        decimal lunchBreakDeduction = totalElapsedHours >= 5.0m ? 1.0m : 0.0m;
                        netHoursWorked = totalElapsedHours - lunchBreakDeduction;
                    }
                }
                else if (employee.OfficeType == "Production")
                {
                    // Production: 8:00 AM - 10:00 AM Flex-in
                    DateTime maxAllowedIn = timeInLocal.Date.AddHours(10);
                    if (timeInLocal > maxAllowedIn)
                    {
                        double prodLateMinutes = Math.Round((timeInLocal - maxAllowedIn).TotalMinutes);
                        lateHoursTotal += (decimal)(prodLateMinutes / 60.0);
                    }

                    double totalElapsedMinutes = Math.Round((timeOutLocal - effectiveTimeIn).TotalMinutes);
                    decimal totalElapsedHours = (decimal)(totalElapsedMinutes / 60.0);
                    decimal lunchBreakDeduction = totalElapsedHours >= 5.0m ? 1.0m : 0.0m;
                    netHoursWorked = totalElapsedHours - lunchBreakDeduction;

                    if (netHoursWorked < 8.0m)
                    {
                        decimal deficitMinutes = (decimal)Math.Round((8.0m - netHoursWorked) * 60.0m);
                        undertimeHoursTotal += deficitMinutes / 60.0m;
                    }
                }
            }
        }

        // 2. Approved Overtime Hours (Strictly from filed & approved overtime records)
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
            overtimeHours = Math.Round(overtimeHours, 2),
            approvedLeaveHours,
            cashAdvanceDeduction,
            regularHolidayHours = Math.Round(regularHolidayHoursTotal, 2),
            specialNonWorkingHours = Math.Round(specialNonWorkingHoursTotal, 2),
            lateHours = Math.Round(lateHoursTotal, 2),
            undertimeHours = Math.Round(undertimeHoursTotal, 2),
            absentDays = 0
        });
    }

    [HttpGet("debug-undertime/{employeeId}")]
    public async Task<IActionResult> DebugUndertime(int employeeId, [FromQuery] string payPeriod = "15th")
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound("Employee not found.");

        DateTime now = DateTime.UtcNow;
        DateTime startDate;
        DateTime endDate;

        if (payPeriod == "15th")
        {
            DateTime prevMonth = now.AddMonths(-1);
            int lastDayPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
            startDate = new DateTime(prevMonth.Year, prevMonth.Month, Math.Min(30, lastDayPrevMonth), 0, 0, 0, DateTimeKind.Utc);
            endDate = new DateTime(now.Year, now.Month, 13, 15, 59, 59, DateTimeKind.Utc);
        }
        else
        {
            startDate = new DateTime(now.Year, now.Month, 14, 0, 0, 0, DateTimeKind.Utc);
            endDate = new DateTime(now.Year, now.Month, 28, 15, 59, 59, DateTimeKind.Utc);
        }

        var periodHolidays = await _context.Holidays
            .Where(h => h.HolidayDate >= startDate && h.HolidayDate <= endDate)
            .ToListAsync();
        var holidayDates = periodHolidays.Select(h => h.HolidayDate.Date).ToHashSet();

        var timeRecords = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId && t.DateCreated >= startDate && t.DateCreated <= endDate)
            .OrderBy(t => t.DateCreated)
            .ToListAsync();

        var groupedLogs = timeRecords.GroupBy(t => t.DateCreated.Date);
        var dailyLogsList = new List<object>();
        decimal totalUndertimeDebug = 0;

        foreach (var group in groupedLogs)
        {
            var dayDate = group.Key;
            var holiday = periodHolidays.FirstOrDefault(h => h.HolidayDate.Date == dayDate);
            var dayLogs = group.OrderBy(t => t.DateCreated).ToList();

            var firstIn = dayLogs.FirstOrDefault(t => t.Type != null && (t.Type.Equals("IN", StringComparison.OrdinalIgnoreCase) || t.Type.Equals("Time IN", StringComparison.OrdinalIgnoreCase)));
            var lastOut = dayLogs.LastOrDefault(t => t.Type != null && (t.Type.Equals("OUT", StringComparison.OrdinalIgnoreCase) || t.Type.Equals("Time OUT", StringComparison.OrdinalIgnoreCase)));

            var timeInUtc = firstIn?.DateCreated;
            var timeOutUtc = lastOut?.DateCreated;

            if (holiday == null && timeInUtc.HasValue && timeOutUtc.HasValue)
            {
                DateTime timeInLocal = timeInUtc.Value.ToUniversalTime().AddHours(8);
                DateTime timeOutLocal = timeOutUtc.Value.ToUniversalTime().AddHours(8);

                decimal dayUndertime = 0;
                string evaluationNote = "Normal";

                if (employee.OfficeType == "Admin")
                {
                    DateTime standardShiftEnd = timeInLocal.Date.AddHours(18); // 6:00 PM

                    if (timeOutLocal < standardShiftEnd)
                    {
                        double undertimeMinutes = Math.Round((standardShiftEnd - timeOutLocal).TotalMinutes);
                        dayUndertime = (decimal)(undertimeMinutes / 60.0);
                        totalUndertimeDebug += dayUndertime;
                        evaluationNote = $"Left early by {undertimeMinutes} mins before 6:00 PM";
                    }
                    else
                    {
                        evaluationNote = "Timed out >= 6:00 PM (0 undertime)";
                    }
                }

                dailyLogsList.Add(new
                {
                    Date = dayDate.ToString("yyyy-MM-dd"),
                    TimeInLocal = timeInLocal.ToString("HH:mm:ss"),
                    TimeOutLocal = timeOutLocal.ToString("HH:mm:ss"),
                    UndertimeAdded = Math.Round(dayUndertime, 4),
                    Note = evaluationNote
                });
            }
        }

        return Ok(new
        {
            Employee = $"{employee.LastName}, {employee.FirstName}",
            CalculatedTotalUndertime = Math.Round(totalUndertimeDebug, 2),
            DailyBreakdown = dailyLogsList
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

        // 1. Spreadsheet-Aligned Rates & Earnings Formulas (D5 equivalent is Actual Daily Rate)
        decimal dailyAllowance = employee.DailyAllowance;
        decimal actualDailyRate = employee.DailySalary + dailyAllowance; // D5 in spreadsheet
        decimal hourlyRate = actualDailyRate / 8.0m; // D5 / 8

        // Basic Pay: (Total Hours / 8) * D5  [where Total Hours = DaysWorked * 8]
        decimal totalBasicHours = queryParams.DaysWorked * 8.0m;
        decimal basicPay = (totalBasicHours / 8.0m) * actualDailyRate;

        // Special Holiday: (Hours / 8) * D5 * 1.3 (matches =(C7/8)*D5*1.3)
        decimal specialHolidayPay = (queryParams.SpecialNonWorkingHours / 8.0m) * actualDailyRate * 1.3m;

        // Regular Holiday: 200% multiplier -> (Hours / 8) * D5 * 2.0
        decimal regularHolidayPay = (queryParams.RegularHolidayHours / 8.0m) * actualDailyRate * 2.0m;

        // Overtime Pay: 125% multiplier -> (Hours / 8) * D5 * 1.25
        decimal overtimePay = (queryParams.OvertimeHours / 8.0m) * actualDailyRate * 1.25m;

        // Leave Pay: (Hours / 8) * D5
        decimal leavePay = (queryParams.ApprovedLeaveHours / 8.0m) * actualDailyRate;

        decimal grossEarnings = basicPay + overtimePay + regularHolidayPay + specialHolidayPay + leavePay;

        // Deductions
        decimal lateDeduction = (queryParams.LateHours / 8.0m) * actualDailyRate;
        decimal undertimeDeduction = (queryParams.UndertimeHours / 8.0m) * actualDailyRate;
        decimal absentDeduction = queryParams.AbsentDays * actualDailyRate;

        // 2. Statutory Government Contributions (Fixed per spreadsheet values)
        decimal sssDeduction = 0;
        decimal philHealthDeduction = 0;
        decimal pagIbigDeduction = 0;

        if (employee.HasGovernmentDeductions)
        {
            // Spreadsheet full monthly baselines
            decimal monthlySss = 720.0m;
            decimal monthlyPhilHealth = 360.0m;
            decimal monthlyPagIbig = 100.0m;

            if (employee.DeductionType == "Per Pay Period")
            {
                // Split across two cutoffs (15th and End of Month)
                sssDeduction = monthlySss / 2.0m;
                philHealthDeduction = monthlyPhilHealth / 2.0m;
                pagIbigDeduction = monthlyPagIbig / 2.0m;
            }
            else if (employee.DeductionType == "Every 15th Pay Period")
            {
                // Full monthly deduction ONLY if current computation is for the 15th pay period
                if (queryParams.PayPeriod == "15th")
                {
                    sssDeduction = monthlySss;
                    philHealthDeduction = monthlyPhilHealth;
                    pagIbigDeduction = monthlyPagIbig;
                }
                else
                {
                    // Zero out for End of Month pay period
                    sssDeduction = 0;
                    philHealthDeduction = 0;
                    pagIbigDeduction = 0;
                }
            }
            else // Default / Monthly
            {
                // Full monthly deduction applied at the end of the month cutoff (or default behavior)
                if (queryParams.PayPeriod != "15th")
                {
                    sssDeduction = monthlySss;
                    philHealthDeduction = monthlyPhilHealth;
                    pagIbigDeduction = monthlyPagIbig;
                }
                else
                {
                    sssDeduction = 0;
                    philHealthDeduction = 0;
                    pagIbigDeduction = 0;
                }
            }
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

        // --- DISPATCH NOTIFICATION TO EMPLOYEE ---
        _context.Notifications.Add(new Notification
        {
            EmployeeId = employeeId,
            Title = "New Pay Slip Available",
            Message = $"Your pay slip for the {periodName} has been generated.",
            Type = "Payroll"
        });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = paySlip.Id,
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

            sssDeduction = Math.Round(sssDeduction, 2),
            philHealthDeduction = Math.Round(philHealthDeduction, 2),
            pagIbigDeduction = Math.Round(pagIbigDeduction, 2),
            governmentContributions = Math.Round(totalGovtContributions, 2),

            totalDeductions = Math.Round(totalDeductions, 2),
            netReceivable = Math.Round(netReceivable, 2)
        });
    }

    [HttpGet("history/{employeeId}")]
    public async Task<IActionResult> GetPayrollHistory(int employeeId, [FromQuery] int page = 1, [FromQuery] int pageSize = 5)
    {
        var query = _context.PaySlips
            .Include(p => p.Employee)
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.DateCreated);

        int totalCount = await query.CountAsync();
        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var history = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        return Ok(new
        {
            items = history,
            totalCount,
            totalPages,
            currentPage = page
        });
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
        if (monthlySalary >= 29750.0m) return 2700.0m;

        decimal msc = Math.Floor((monthlySalary - 4250.0m) / 500.0m) * 500.0m + 4500.0m;
        return msc * 0.045m;
    }

    [HttpGet("status-summary")]
    public async Task<IActionResult> GetPayrollStatusSummary([FromQuery] string payPeriod = "15th")
    {
        string periodName = payPeriod == "15th" ? "15th Pay Period" : "End of Month Pay Period";
        DateTime now = DateTime.UtcNow;

        // Find all employee IDs with generated payslips for this period and month
        var generatedSlips = await _context.PaySlips
            .Where(p => p.PayPeriod == periodName &&
                        p.PayPeriodEnd.Month == now.Month &&
                        p.PayPeriodEnd.Year == now.Year)
            .Select(p => p.EmployeeId)
            .Distinct()
            .ToListAsync();

        return Ok(generatedSlips);
    }

    private const string Navy = "#1F2A44";
    private const string Accent = "#F5A623";
    private const string AccentBg = "#FFF6E5";
    private const string Ink = "#1F2937";
    private const string Muted = "#6B7280";
    private const string Line = "#E5E7EB";
    private const string Zebra = "#F8FAFC";
    private const string Danger = "#B91C1C";
    private const string Success = "#047857";

    private static string Money(IFormattable value) =>
        "₱" + value.ToString("N2", CultureInfo.InvariantCulture);

    private static void InfoBlock(IContainer container, string label, string value, string? sub = null)
    {
        container.Column(c =>
        {
            c.Item().Text(label.ToUpper()).FontSize(7).SemiBold().LetterSpacing(0.08f).FontColor(Muted);
            c.Item().PaddingTop(2).Text(value).FontSize(11).Bold().FontColor(Ink);
            if (sub != null)
                c.Item().PaddingTop(1).Text(sub).FontSize(7.5f).FontColor(Muted);
        });
    }

    private static void Section(
        IContainer container,
        string title,
        (string Label, string Amount)[] items,
        string totalLabel,
        string totalAmount,
        string totalColor)
    {
        container
            .Border(1).BorderColor(Line)
            .CornerRadius(6)
            .Column(col =>
            {
                // Section header
                col.Item().Background(Navy).PaddingVertical(8).PaddingHorizontal(12)
                    .Text(title.ToUpper()).FontSize(8).Bold().LetterSpacing(0.08f).FontColor(Colors.White);

                // Zebra-striped line items
                for (int i = 0; i < items.Length; i++)
                {
                    var (label, amount) = items[i];
                    col.Item()
                        .Background(i % 2 == 0 ? Colors.White : Zebra)
                        .PaddingVertical(6).PaddingHorizontal(12)
                        .Row(r =>
                        {
                            r.RelativeItem().Text(label).FontSize(9).FontColor(Ink);
                            r.AutoItem().Text(amount).FontSize(9).FontColor(Ink);
                        });
                }

                // Total row
                col.Item().BorderTop(1.5f).BorderColor(Navy)
                    .Background(Zebra)
                    .PaddingVertical(8).PaddingHorizontal(12)
                    .Row(r =>
                    {
                        r.RelativeItem().Text(totalLabel).FontSize(9.5f).Bold().FontColor(Ink);
                        r.AutoItem().Text(totalAmount).FontSize(9.5f).Bold().FontColor(totalColor);
                    });
            });
    }

    [HttpGet("download-payslip/{idOrPeriod}")]
    public async Task<IActionResult> DownloadPayslipPdf(string idOrPeriod, [FromQuery] int? employeeId = null, [FromQuery] string? payPeriod = null)
    {
        PaySlip? payrollRecord = null;

        // Numeric database ID
        if (int.TryParse(idOrPeriod, out int recordId) && recordId != 999)
        {
            payrollRecord = await _context.PaySlips
                .Include(p => p.Employee)
                .FirstOrDefaultAsync(p => p.Id == recordId);
        }

        // Fallback: placeholder (999) or period name → lookup by employee & period
        if (payrollRecord == null && employeeId.HasValue)
        {
            string targetPeriod = payPeriod ?? (idOrPeriod.Contains("15th") ? "15th Pay Period" : "End of Month Pay Period");
            DateTime now = DateTime.UtcNow;

            payrollRecord = await _context.PaySlips
                .Include(p => p.Employee)
                .Where(p => p.EmployeeId == employeeId.Value &&
                            p.PayPeriod.ToLower().Contains(targetPeriod.ToLower()) &&
                            p.PayPeriodEnd.Month == now.Month &&
                            p.PayPeriodEnd.Year == now.Year)
                .OrderByDescending(p => p.DateCreated)
                .FirstOrDefaultAsync();
        }

        if (payrollRecord == null) return NotFound("Payslip record not found.");

        // Names / filename
        string employeeName = payrollRecord.Employee != null
            ? $"{payrollRecord.Employee.LastName}_{payrollRecord.Employee.FirstName}"
            : "Employee";

        string cleanEmployeeName = employeeName.Replace(" ", "_").Replace(",", "").Replace(".", "");
        string payslipType = payrollRecord.PayPeriod != null && payrollRecord.PayPeriod.Contains("15th") ? "15th" : "14-28";
        string currentDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string filename = $"{cleanEmployeeName}_{payslipType}_{currentDate}.pdf";

        string displayName = employeeName.Replace("_", " ");
        string department = $"{payrollRecord.Employee?.OfficeType ?? "General"} Staff";
        string periodEnd = Convert.ToDateTime(payrollRecord.PayPeriodEnd).ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture);
        string dailyRate = Money(payrollRecord.DailySalary + payrollRecord.DailyAllowance);

        var earnings = new (string, string)[]
        {
        ("Basic Pay",        Money(payrollRecord.BasicPay)),
        ("Overtime Pay",     Money(payrollRecord.OvertimePay)),
        ("Regular Holiday",  Money(payrollRecord.RegularHolidayPay)),
        ("Special Holiday",  Money(payrollRecord.SpecialHolidayPay)),
        ("Approved Leave",   Money(payrollRecord.LeavePay)),
        };

        var deductions = new (string, string)[]
        {
        ("Late / Undertime",                     Money(payrollRecord.LateDeduction + payrollRecord.UndertimeDeduction)),
        ("Absent Deductions",                    Money(payrollRecord.AbsentDeduction)),
        ("Cash Advance",                         Money(payrollRecord.CashAdvanceDeduction)),
        ("Govt (SSS / PhilHealth / Pag-IBIG)", Money(payrollRecord.GovernmentContributions)),
        };

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0); // full-bleed header band; padding is applied per section
                page.PageColor(Colors.White);
                // Font fallbacks so the ₱ glyph renders on Windows and Linux hosts
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9).FontColor(Ink));

                // ── HEADER ───────────────────────────────────────
                page.Header().Column(header =>
                {
                    header.Item().Background(Navy).PaddingHorizontal(32).PaddingVertical(22).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Firefly Crafts PH").FontSize(18).Bold().FontColor(Colors.White);
                            col.Item().PaddingTop(2).Text("NXF Sticker Shop").FontSize(9).SemiBold().FontColor(Accent);
                            col.Item().PaddingTop(6).Text("Unit #26, 2nd Flr, J&G Bldg, H. Abellana St., Canduman, Mandaue City")
                                .FontSize(7.5f).FontColor(Colors.Grey.Lighten2);
                        });

                        row.ConstantItem(190).AlignRight().Column(col =>
                        {
                            col.Item().AlignRight().Text("PAYSLIP").FontSize(24).Bold().LetterSpacing(0.1f).FontColor(Colors.White);
                            col.Item().AlignRight().PaddingTop(2).Text(payrollRecord.PayPeriod ?? "").FontSize(9).SemiBold().FontColor(Accent);
                            col.Item().AlignRight().Text($"Period ending {periodEnd}").FontSize(8).FontColor(Colors.Grey.Lighten2);
                        });
                    });

                    header.Item().Height(4).Background(Accent);
                });

                // ── CONTENT ──────────────────────────────────────
                page.Content().PaddingHorizontal(32).PaddingVertical(22).Column(col =>
                {
                    col.Spacing(18);

                    // Employee summary card
                    col.Item()
                        .Background(Zebra)
                        .BorderLeft(4).BorderColor(Accent)
                        .Border(1).BorderColor(Line)
                        .Padding(14)
                        .Row(row =>
                        {
                            row.RelativeItem(3).Element(e => InfoBlock(e, "Employee", displayName));
                            row.RelativeItem(2).Element(e => InfoBlock(e, "Department", department));
                            row.RelativeItem(2).Element(e => InfoBlock(
                                e, "Daily Rate", dailyRate,
                                $"Base {Money(payrollRecord.DailySalary)} + Allow. {Money(payrollRecord.DailyAllowance)}"));
                        });

                    // Earnings & deductions side by side
                    col.Item().Row(row =>
                    {
                        row.Spacing(14);

                        row.RelativeItem().Element(e => Section(
                            e, "Earnings & Additions", earnings,
                            "Gross Earnings", Money(payrollRecord.GrossEarnings), Success));

                        row.RelativeItem().Element(e => Section(
                            e, "Deductions & Contributions", deductions,
                            "Total Deductions", Money(payrollRecord.TotalDeductions), Danger));
                    });

                    // Net pay banner
                    col.Item()
                        .Background(Navy)
                        .CornerRadius(8)
                        .Padding(18)
                        .Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("NET RECEIVABLE PAY").FontSize(10).Bold().LetterSpacing(0.08f).FontColor(Accent);
                                c.Item().PaddingTop(2).Text("Total take-home pay for this pay period")
                                    .FontSize(8).FontColor(Colors.Grey.Lighten2);
                            });

                            row.AutoItem().AlignMiddle().Text(Money(payrollRecord.NetReceivable))
                                .FontSize(22).Bold().FontColor(Colors.White);
                        });

                    // Signatures
                    col.Item().PaddingTop(28).Row(row =>
                    {
                        row.Spacing(50);

                        foreach (var label in new[] { "Employee Signature", "Authorized by (Employer)" })
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().BorderBottom(1).BorderColor(Muted).Height(28);
                                c.Item().PaddingTop(4).AlignCenter().Text(label).FontSize(8).FontColor(Muted);
                            });
                        }
                    });
                });

                // ── FOOTER ───────────────────────────────────────
                page.Footer().PaddingHorizontal(32).PaddingBottom(16).Column(f =>
                {
                    f.Item().LineHorizontal(0.75f).LineColor(Line);
                    f.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().Text("Confidential — for the named employee only. Please report any discrepancies to HR.")
                            .FontSize(7).FontColor(Muted);

                        row.AutoItem().Text(text =>
                        {
                            text.DefaultTextStyle(s => s.FontSize(7).FontColor(Muted));
                            text.Span($"Generated {DateTime.UtcNow:MMM dd, yyyy}  •  Page ");
                            text.CurrentPageNumber();
                            text.Span(" of ");
                            text.TotalPages();
                        });
                    });
                });
            });
        });

        byte[] pdfBytes = document.GeneratePdf();
        return File(pdfBytes, "application/pdf", filename);
    }

    [HttpGet("download-payslip/employee/{employeeId}")]
    public async Task<IActionResult> DownloadPayslipByEmployeePdf(int employeeId, [FromQuery] string payPeriod = "15th")
    {
        string periodName = payPeriod.Contains("15th") ? "15th Pay Period" : "End of Month Pay Period";
        DateTime now = DateTime.UtcNow;

        var payrollRecord = await _context.PaySlips
            .Include(p => p.Employee)
            .Where(p => p.EmployeeId == employeeId &&
                        p.PayPeriod == periodName &&
                        p.PayPeriodEnd.Month == now.Month &&
                        p.PayPeriodEnd.Year == now.Year)
            .OrderByDescending(p => p.DateCreated)
            .FirstOrDefaultAsync();

        if (payrollRecord == null) return NotFound("Payslip record not found for this period.");

        return await DownloadPayslipPdf(payrollRecord.Id.ToString());
    }
}