namespace FireflyHR.API.Models;

public class EmployeeUpdateDto
{
    public string CurrentAddress { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string PersonalEmailAddress { get; set; } = string.Empty;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactNumber { get; set; } = string.Empty;
    public string RelationToEmployee { get; set; } = string.Empty;
    public string EmergencyContactAddress { get; set; } = string.Empty;
}