namespace FireflyHR.API.Models;

public class Notification
{
    public int Id { get; set; }
    public int EmployeeId { get; set; } // Who the notification is for (or Admin ID)
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Info"; // Leave, Overtime, Attendance, etc.
    public bool IsRead { get; set; } = false;
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}