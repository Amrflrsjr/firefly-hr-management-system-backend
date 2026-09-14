using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class TimeRecordsController : ControllerBase
{
    private readonly AppDbContext _context;

    public TimeRecordsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("time-in-out")]
    public async Task<ActionResult<TimeRecord>> RecordTime(TimeRecord timeRecord)
    {
        var today = DateTime.UtcNow.Date;

        if (timeRecord.Type == "IN")
        {
            var existingIn = await _context.TimeRecords
                .FirstOrDefaultAsync(t => t.EmployeeId == timeRecord.EmployeeId &&
                                          t.Type == "IN" &&
                                          t.DateCreated.Date == today);

            if (existingIn != null)
            {
                return BadRequest("You have already clocked in for today.");
            }
        }
        else if (timeRecord.Type == "OUT")
        {
            var existingOut = await _context.TimeRecords
                .FirstOrDefaultAsync(t => t.EmployeeId == timeRecord.EmployeeId &&
                                          t.Type == "OUT" &&
                                          t.DateCreated.Date == today);

            if (existingOut != null)
            {
                return BadRequest("You have already clocked out for today.");
            }
        }

        timeRecord.DateCreated = DateTime.UtcNow;
        _context.TimeRecords.Add(timeRecord);
        await _context.SaveChangesAsync();
        return Ok(timeRecord);
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> PostTimeRecordsBulk([FromBody] List<TimeRecord> records)
    {
        foreach (var record in records)
        {
            record.DateCreated = DateTime.SpecifyKind(record.DateCreated, DateTimeKind.Utc);
        }
        _context.TimeRecords.AddRange(records);
        await _context.SaveChangesAsync();
        return Ok(new { Message = $"{records.Count} time records added successfully." });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimeRecord>>> GetTimeRecords() =>
        await _context.TimeRecords.Include(t => t.Employee).ToListAsync();

    [HttpGet("employee/{employeeId}")]
    public async Task<ActionResult<IEnumerable<TimeRecord>>> GetEmployeeTimeRecords(int employeeId)
    {
        var records = await _context.TimeRecords
            .Include(t => t.Employee)
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.DateCreated)
            .ToListAsync();

        return Ok(records);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTimeRecord(int id, TimeRecord timeRecord)
    {
        if (id != timeRecord.Id) return BadRequest();
        _context.Entry(timeRecord).State = EntityState.Modified;
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.TimeRecords.Any(e => e.Id == id)) return NotFound();
            throw;
        }
        return NoContent();
    }

    [Authorize]
    [HttpGet("latest-today/{employeeId}")]
    public async Task<IActionResult> GetLatestTimeInToday(int employeeId)
    {
        var today = DateTime.UtcNow.Date;
        var latestRecord = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId && t.DateCreated.Date == today)
            .OrderByDescending(t => t.DateCreated)
            .FirstOrDefaultAsync();

        return Ok(latestRecord);
    }

    [Authorize]
    [HttpGet("dashboard-metrics/{employeeId}")]
    public async Task<IActionResult> GetDashboardMetrics(int employeeId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound("Employee not found.");

        DateTime now = DateTime.UtcNow;
        DateTime startDate = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime endDate = now.Date;

        // Fetch today's logs for this specific employee
        var todayLogs = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId && t.DateCreated.Date == now.Date)
            .OrderBy(t => t.DateCreated)
            .ToListAsync();

        var firstInToday = todayLogs.FirstOrDefault(t => t.Type == "IN");
        var lastOutToday = todayLogs.LastOrDefault(t => t.Type == "OUT");

        string? lastTimeInStr = firstInToday?.DateCreated.ToLocalTime().ToString("hh:mm tt");
        string? lastTimeOutStr = lastOutToday?.DateCreated.ToLocalTime().ToString("hh:mm tt");

        bool hasClockedInToday = firstInToday != null;
        bool hasClockedOutToday = lastOutToday != null;

        // Calculate missed records
        var recordedDays = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId && t.DateCreated >= startDate && t.DateCreated <= endDate && t.Type == "IN")
            .Select(t => t.DateCreated.Date)
            .Distinct()
            .ToListAsync();

        int missedCount = 0;
        for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;
            if (!recordedDays.Contains(date))
            {
                missedCount++;
            }
        }

        return Ok(new
        {
            regularHours = 33.5,
            overtimeHours = 4.0,
            totalHours = 37.5,
            estimatedPayout = 4550.0,
            lastTimeIn = lastTimeInStr,
            lastTimeOut = lastTimeOutStr,
            hasClockedInToday,
            hasClockedOutToday,
            missedRecordsCount = missedCount
        });
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTimeRecord(int id)
    {
        var record = await _context.TimeRecords.FindAsync(id);
        if (record == null) return NotFound();
        _context.TimeRecords.Remove(record);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}