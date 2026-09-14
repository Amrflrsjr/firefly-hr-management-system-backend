namespace FireflyHR.API.Models;

public class Holiday
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime HolidayDate { get; set; }
    public string HolidayType { get; set; } = "Regular"; // Regular, Special Working, Special Non-Working, Firefly
}