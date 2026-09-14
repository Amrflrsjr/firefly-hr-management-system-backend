namespace FireflyHR.API.Models;

public class PaySlip
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public string PayPeriod { get; set; } = string.Empty;
    public DateTime PayPeriodEnd { get; set; }

    // Earnings Breakdown
    public decimal DailySalary { get; set; }
    public decimal BasicPay { get; set; }
    public decimal OvertimePay { get; set; }
    public decimal RegularHolidayPay { get; set; }
    public decimal SpecialHolidayPay { get; set; }
    public decimal LeavePay { get; set; }
    public decimal GrossEarnings { get; set; }

    // Deductions Breakdown
    public decimal LateDeduction { get; set; }
    public decimal UndertimeDeduction { get; set; }
    public decimal AbsentDeduction { get; set; }
    public decimal CashAdvanceDeduction { get; set; }
    public decimal GovernmentContributions { get; set; }
    public decimal TotalDeductions { get; set; }

    // Final Total
    public decimal NetReceivable { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}