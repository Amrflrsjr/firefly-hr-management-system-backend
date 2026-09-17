using Microsoft.EntityFrameworkCore;
using FireflyHR.API.Models;

namespace FireflyHR.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Employee> Employees { get; set; } = null!;
    public DbSet<Holiday> Holidays { get; set; } = null!;
    public DbSet<TimeRecord> TimeRecords { get; set; } = null!;
    public DbSet<Leave> Leaves { get; set; } = null!;
    public DbSet<Overtime> Overtimes { get; set; } = null!;
    public DbSet<CashAdvance> CashAdvances { get; set; } = null!;
    public DbSet<PaySlip> PaySlips => Set<PaySlip>();
    public DbSet<AttendanceRequest> AttendanceRequests { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; }
}