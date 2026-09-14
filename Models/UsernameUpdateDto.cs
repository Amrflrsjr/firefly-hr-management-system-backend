namespace FireflyHR.API.Models;

public class UsernameUpdateDto
{
    public int EmployeeId { get; set; }
    public string NewUsername { get; set; } = string.Empty;
}