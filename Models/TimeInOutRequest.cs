namespace FireflyHR.API.Models;

public class TimeInOutRequest
{
    public int EmployeeId { get; set; }
    public string Type { get; set; } = "IN"; // IN or OUT
    public DateTime? DateCreated { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}