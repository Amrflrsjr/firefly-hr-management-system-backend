using ClosedXML.Excel;
using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;

namespace FireflyHR.API.Controllers;

public class TimeInOutRequest
{
    public int EmployeeId { get; set; }
    public string Type { get; set; } = "IN"; // IN or OUT
    public DateTime? DateCreated { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

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

    // Helper method to safely convert UTC to Philippine Standard Time across Windows and Linux (AWS)
    private static DateTime GetPstTime(DateTime utcDateTime)
    {
        TimeZoneInfo pstZone = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, pstZone);
    }

    [HttpPost("time-in-out")]
    public async Task<ActionResult<TimeRecord>> RecordTime([FromBody] TimeInOutRequest request)
    {
        DateTime recordUtc = request.DateCreated != default && request.DateCreated.HasValue
            ? DateTime.SpecifyKind(request.DateCreated.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;

        DateTime targetPstDate = DateTime.SpecifyKind(GetPstTime(recordUtc).Date, DateTimeKind.Utc);

        var existingLogs = await _context.TimeRecords
            .Where(t => t.EmployeeId == request.EmployeeId)
            .ToListAsync();

        // Check duplicate log type
        bool duplicateLog = existingLogs.Any(t =>
            GetPstTime(t.DateCreated).Date == targetPstDate &&
            t.Type.Equals(request.Type, StringComparison.OrdinalIgnoreCase));

        if (duplicateLog)
        {
            return BadRequest($"A Time {request.Type} record already exists for {targetPstDate:MMM dd, yyyy}. Delete it first.");
        }

        // Chronological Order Validation
        var existingIn = existingLogs.FirstOrDefault(t => t.Type == "IN" && GetPstTime(t.DateCreated).Date == targetPstDate);
        var existingOut = existingLogs.FirstOrDefault(t => t.Type == "OUT" && GetPstTime(t.DateCreated).Date == targetPstDate);

        if (request.Type == "OUT" && existingIn != null && recordUtc <= existingIn.DateCreated)
        {
            return BadRequest($"Time OUT must be later than Time IN ({GetPstTime(existingIn.DateCreated):hh:mm tt}).");
        }

        if (request.Type == "IN" && existingOut != null && recordUtc >= existingOut.DateCreated)
        {
            return BadRequest($"Time IN must be earlier than Time OUT ({GetPstTime(existingOut.DateCreated):hh:mm tt}).");
        }

        var timeRecord = new TimeRecord
        {
            EmployeeId = request.EmployeeId,
            Type = request.Type,
            DateCreated = recordUtc,
            Date = targetPstDate,
            Latitude = request.Latitude,
            Longitude = request.Longitude
        };

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
    public async Task<IActionResult> GetTimeRecords([FromQuery] string? date = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 15)
    {
        var query = _context.TimeRecords
            .Include(t => t.Employee)
            .OrderByDescending(t => t.DateCreated)
            .AsQueryable();

        if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var parsedDate))
        {
            var allRecords = await query.ToListAsync();
            var filtered = allRecords.Where(t => GetPstTime(t.DateCreated).Date == parsedDate.Date).ToList();

            int totalCount = filtered.Count;
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var pagedItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new { items = pagedItems, totalCount, totalPages, currentPage = page });
        }

        int totalCountDb = await query.CountAsync();
        int totalPagesDb = (int)Math.Ceiling(totalCountDb / (double)pageSize);
        var pagedRecords = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { items = pagedRecords, totalCount = totalCountDb, totalPages = totalPagesDb, currentPage = page });
    }

    [HttpGet("employee/{employeeId}")]
    public async Task<IActionResult> GetEmployeeTimeRecords(
    int employeeId,
    [FromQuery] string? date = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 15)
    {
        var query = _context.TimeRecords
            .Include(t => t.Employee)
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.DateCreated)
            .AsQueryable();

        if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var parsedDate))
        {
            var allRecords = await query.ToListAsync();
            var filtered = allRecords.Where(t => GetPstTime(t.DateCreated).Date == parsedDate.Date).ToList();

            int totalCount = filtered.Count;
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            var pagedItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new { items = pagedItems, totalCount, totalPages, currentPage = page });
        }

        int totalCountDb = await query.CountAsync();
        int totalPagesDb = (int)Math.Ceiling(totalCountDb / (double)pageSize);
        var pagedRecords = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new { items = pagedRecords, totalCount = totalCountDb, totalPages = totalPagesDb, currentPage = page });
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
        DateTime todayPst = GetPstTime(DateTime.UtcNow).Date;

        var allRecords = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.DateCreated)
            .ToListAsync();

        var latestRecord = allRecords.FirstOrDefault(t => GetPstTime(t.DateCreated).Date == todayPst);

        return Ok(latestRecord);
    }

    [Authorize]
    [HttpGet("dashboard-metrics/{employeeId}")]
    public async Task<IActionResult> GetDashboardMetrics(int employeeId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound("Employee not found.");

        DateTime nowPst = GetPstTime(DateTime.UtcNow);
        DateTime startDatePst = new DateTime(nowPst.Year, nowPst.Month, 1).Date;
        DateTime endDatePst = nowPst.Date;

        // Fetch logs for this month
        var monthLogs = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId && t.DateCreated >= startDatePst.ToUniversalTime())
            .ToListAsync();

        // Filter logs matching PST today
        var todayLogs = monthLogs
            .Where(t => GetPstTime(t.DateCreated).Date == endDatePst)
            .OrderBy(t => t.DateCreated)
            .ToList();

        var firstInToday = todayLogs.FirstOrDefault(t => t.Type == "IN");
        var lastOutToday = todayLogs.LastOrDefault(t => t.Type == "OUT");

        string? lastTimeInStr = firstInToday != null
            ? GetPstTime(firstInToday.DateCreated).ToString("hh:mm tt")
            : null;

        string? lastTimeOutStr = lastOutToday != null
            ? GetPstTime(lastOutToday.DateCreated).ToString("hh:mm tt")
            : null;

        bool hasClockedInToday = firstInToday != null;
        bool hasClockedOutToday = lastOutToday != null;

        // Dynamically compute regular hours, overtime hours, and total hours from logs
        double regularHours = 0;
        double overtimeHours = 0;

        var groupedByDate = monthLogs
            .GroupBy(t => GetPstTime(t.DateCreated).Date)
            .Where(g => g.Key <= endDatePst);

        foreach (var group in groupedByDate)
        {
            var dayIns = group.Where(t => t.Type == "IN").OrderBy(t => t.DateCreated).ToList();
            var dayOuts = group.Where(t => t.Type == "OUT").OrderBy(t => t.DateCreated).ToList();

            if (dayIns.Any() && dayOuts.Any())
            {
                var firstIn = dayIns.First().DateCreated;
                var lastOut = dayOuts.Last().DateCreated;

                if (lastOut > firstIn)
                {
                    double hours = (lastOut - firstIn).TotalHours;

                    // Optional: Deduct 1 hour meal break if shift exceeds 6 hours
                    if (hours > 6.0) hours -= 1.0;

                    if (hours > 8.0)
                    {
                        regularHours += 8.0;
                        overtimeHours += (hours - 8.0);
                    }
                    else
                    {
                        regularHours += Math.Max(0, hours);
                    }
                }
            }
        }

        double totalHours = regularHours + overtimeHours;

        // Dynamically compute estimated payout based on DailySalary (assuming 8 hours/day)
        double hourlyRate = employee.DailySalary > 0 ? (double)(employee.DailySalary / 8.0m) : 0;
        double estimatedPayout = (regularHours * hourlyRate) + (overtimeHours * hourlyRate * 1.25);

        // Calculate missed records based on PST calendar dates (excluding weekends)
        var recordedDaysPst = monthLogs
            .Where(t => t.Type == "IN")
            .Select(t => GetPstTime(t.DateCreated).Date)
            .Distinct()
            .ToList();

        int missedCount = 0;
        for (DateTime date = startDatePst; date < endDatePst; date = date.AddDays(1))
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;
            if (!recordedDaysPst.Contains(date))
            {
                missedCount++;
            }
        }

        return Ok(new
        {
            regularHours = Math.Round(regularHours, 1),
            overtimeHours = Math.Round(overtimeHours, 1),
            totalHours = Math.Round(totalHours, 1),
            estimatedPayout = Math.Round(estimatedPayout, 2),
            lastTimeIn = lastTimeInStr,
            lastTimeOut = lastTimeOutStr,
            hasClockedInToday,
            hasClockedOutToday,
            missedRecordsCount = missedCount
        });
    }

    [HttpGet("export/employee/{employeeId}")]
    public async Task<IActionResult> ExportTimesheetToExcel(int employeeId)
    {
        var employee = await _context.Employees.FindAsync(employeeId);
        if (employee == null) return NotFound("Employee not found.");

        var records = await _context.TimeRecords
            .Where(t => t.EmployeeId == employeeId)
            .OrderByDescending(t => t.DateCreated)
            .ToListAsync();

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Timesheet");

            worksheet.Cell(1, 1).Value = "Log ID";
            worksheet.Cell(1, 2).Value = "Employee Name";
            worksheet.Cell(1, 3).Value = "Log Type";
            worksheet.Cell(1, 4).Value = "Date Logged";
            worksheet.Cell(1, 5).Value = "Timestamp";
            worksheet.Cell(1, 6).Value = "Coordinates (Lat, Lon)";

            var headerRange = worksheet.Range(1, 1, 1, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
            headerRange.Style.Font.FontColor = XLColor.White;

            int row = 2;
            foreach (var rec in records)
            {
                DateTime dtPst = GetPstTime(rec.DateCreated);

                worksheet.Cell(row, 1).Value = rec.Id;
                worksheet.Cell(row, 2).Value = $"{employee.LastName}, {employee.FirstName}";
                worksheet.Cell(row, 3).Value = $"Time {rec.Type}";
                worksheet.Cell(row, 4).Value = dtPst.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 5).Value = dtPst.ToString("hh:mm:ss tt");
                worksheet.Cell(row, 6).Value = $"{rec.Latitude}, {rec.Longitude}";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                var content = stream.ToArray();
                string fileName = $"Timesheet_{employee.LastName}_{employee.FirstName}_{DateTime.UtcNow:yyyyMMdd}.xlsx";

                return File(
                    content,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName
                );
            }
        }
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