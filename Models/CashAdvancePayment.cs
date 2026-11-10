using System.Text.Json.Serialization;

namespace FireflyHR.API.Models;

public class CashAdvancePayment
{
    public int Id { get; set; }
    public int CashAdvanceId { get; set; }

    [JsonIgnore]
    public CashAdvance? CashAdvance { get; set; }

    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public string? Remarks { get; set; }
}