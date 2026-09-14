namespace FireflyHR.API.Models;

public class AttendanceRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string Type { get; set; } = string.Empty; // "IN" or "OUT"
    public DateTime TargetDate { get; set; }
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
    public DateTime DateRequested { get; set; } = DateTime.UtcNow;
}