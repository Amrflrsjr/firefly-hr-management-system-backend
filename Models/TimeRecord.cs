namespace FireflyHR.API.Models;

public class TimeRecord
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime Date { get; set; }
    public string Type { get; set; } = "IN"; // IN or OUT
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}