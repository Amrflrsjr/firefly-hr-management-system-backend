namespace FireflyHR.API.Models;

public class CalculatePayrollParams
{
    public string PayPeriod { get; set; } = "15th";
    public int DaysWorked { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal RegularHolidayHours { get; set; }
    public decimal SpecialNonWorkingHours { get; set; }
    public decimal ApprovedLeaveHours { get; set; }
    public decimal LateHours { get; set; }
    public decimal UndertimeHours { get; set; }
    public int AbsentDays { get; set; }
    public decimal CashAdvanceDeduction { get; set; }
}