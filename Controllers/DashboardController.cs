using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FireflyHR.API.Data;
using System.Runtime.InteropServices;

namespace FireflyHR.API.Controllers;

[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
[ApiController]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public DashboardController(AppDbContext context)
    {
        _context = context;
    }

    private static DateTime GetPstTime(DateTime utcDateTime)
    {
        TimeZoneInfo pstZone = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, pstZone);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetDashboardStats()
    {
        var totalEmployees = await _context.Employees.CountAsync(e => !e.IsAdmin);
        var pendingAttendance = await _context.AttendanceRequests.CountAsync(a => a.Status == "In Review");

        // Count pending leave requests ("In Review")
        var pendingLeaves = await _context.Leaves.CountAsync(l => l.Status == "In Review");

        var employees = await _context.Employees.Where(e => !e.IsAdmin).ToListAsync();
        decimal totalEstMonthlyPayroll = employees.Sum(e =>
        {
            decimal dailyTotal = (e.DailySalary > 0 ? e.DailySalary : 600m) + e.DailyAllowance;
            return dailyTotal * 26.0m;
        });

        return Ok(new
        {
            totalEmployees,
            pendingAttendanceRequests = pendingAttendance,
            pendingLeaves,
            totalPayrollThisMonth = Math.Round(totalEstMonthlyPayroll, 2)
        });
    }

    [HttpGet("attendance-trends")]
    public async Task<IActionResult> GetAttendanceTrends()
    {
        // Get current week's Monday to Friday dates
        DateTime nowPst = GetPstTime(DateTime.UtcNow);
        int diff = (7 + (int)nowPst.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        DateTime startOfWeekPst = nowPst.Date.AddDays(-diff);

        var activeEmployeesCount = await _context.Employees.CountAsync(e => !e.IsAdmin);
        if (activeEmployeesCount == 0) activeEmployeesCount = 1;

        var weeklyTrends = new List<object>();
        string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri" };

        for (int i = 0; i < 5; i++)
        {
            DateTime currentDayPst = startOfWeekPst.AddDays(i);

            // Count unique employees who clocked in on this day
            var records = await _context.TimeRecords
                .Where(t => t.Type == "IN")
                .ToListAsync();

            int clockedInCount = records
                .Where(t => GetPstTime(t.DateCreated).Date == currentDayPst.Date)
                .Select(t => t.EmployeeId)
                .Distinct()
                .Count();

            int rate = Math.Min(100, (int)Math.Round((double)clockedInCount / activeEmployeesCount * 100));

            if (currentDayPst > nowPst.Date)
            {
                rate = 0;
            }

            weeklyTrends.Add(new { day = days[i], attendanceRate = rate });
        }

        return Ok(weeklyTrends);
    }

    [HttpGet("leave-distribution")]
    public async Task<IActionResult> GetLeaveDistribution()
    {
        var distribution = await _context.Leaves
            .GroupBy(l => l.LeaveType)
            .Select(g => new {
                Type = g.Key,
                Count = g.Count(),
                Color = g.Key == "Vacation" ? "bg-sky-500" : g.Key == "Sick Leave" ? "bg-amber-500" : "bg-rose-500"
            })
            .ToListAsync();

        return Ok(distribution);
    }

    [HttpGet("payroll-summary")]
    public async Task<IActionResult> GetPayrollSummary()
    {
        var employees = await _context.Employees
            .Where(e => !e.IsAdmin)
            .ToListAsync();

        decimal totalEstMonthlyPayroll = employees.Sum(e =>
        {
            decimal dailyTotal = (e.DailySalary > 0 ? e.DailySalary : 600m) + e.DailyAllowance;
            return dailyTotal * 26.0m;
        });

        return Ok(new { totalPayrollThisMonth = Math.Round(totalEstMonthlyPayroll, 2) });
    }

    [HttpGet("payroll-trends")]
    public async Task<IActionResult> GetPayrollTrends()
    {
        var now = DateTime.UtcNow;

        var currentMonthPaySlips = await _context.PaySlips
            .Where(p => p.PayPeriodEnd.Month == now.Month && p.PayPeriodEnd.Year == now.Year)
            .ToListAsync();

        decimal currentEst = currentMonthPaySlips.Sum(p => p.NetReceivable);

        if (currentEst == 0)
        {
            var activeEmployees = await _context.Employees.Where(e => !e.IsAdmin).ToListAsync();
            currentEst = activeEmployees.Sum(e => (e.DailySalary + e.DailyAllowance) * 13);
        }

        var prevMonth = now.AddMonths(-1);
        var prevMonthPaySlips = await _context.PaySlips
            .Where(p => p.PayPeriodEnd.Month == prevMonth.Month && p.PayPeriodEnd.Year == prevMonth.Year)
            .ToListAsync();

        decimal prevMonthTotal = prevMonthPaySlips.Sum(p => p.NetReceivable);
        if (prevMonthTotal == 0) prevMonthTotal = currentEst * 0.9m;

        var trends = new[]
        {
            new { period = "Prev Cutoff", amount = Math.Round(prevMonthTotal * 0.5m, 2) },
            new { period = "15th Cutoff", amount = Math.Round(prevMonthTotal, 2) },
            new { period = "Current Est.", amount = Math.Round(currentEst, 2) }
        };

        return Ok(trends);
    }
}