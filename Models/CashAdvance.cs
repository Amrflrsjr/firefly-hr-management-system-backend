namespace FireflyHR.API.Models;

public class CashAdvance
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public decimal CashAdvanceAmount { get; set; }
    public decimal RemainingBalance { get; set; }
    public string DeductionType { get; set; } = "Monthly"; // Monthly or Per Pay Period
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Active"; // Active or Paid
}