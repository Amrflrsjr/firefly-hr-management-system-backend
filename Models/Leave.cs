namespace FireflyHR.API.Models;

public class Leave
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime LeaveDate { get; set; }
    public decimal LeaveHours { get; set; }
    public string Status { get; set; } = "In Review"; // In Review or Approved
}