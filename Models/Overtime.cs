namespace FireflyHR.API.Models;

public class Overtime
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public DateTime OvertimeDate { get; set; }
    public decimal OvertimeHours { get; set; }
    public string Status { get; set; } = "In Review"; // In Review or Approved
}