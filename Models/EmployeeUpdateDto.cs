namespace FireflyHR.API.Models;

public class EmployeeUpdateDto
{
    public string Username { get; set; } = string.Empty;
    public string CurrentAddress { get; set; } = string.Empty;
    public string PermanentAddress { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string PersonalEmailAddress { get; set; } = string.Empty;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactNumber { get; set; } = string.Empty;
    public string RelationToEmployee { get; set; } = string.Empty;
    public string EmergencyContactAddress { get; set; } = string.Empty;

    public string SssNumber { get; set; } = string.Empty;
    public string PhilHealthNumber { get; set; } = string.Empty;
    public string PagIbigNumber { get; set; } = string.Empty;

    // Added fields
    public string Gender { get; set; } = string.Empty;
    public string CivilStatus { get; set; } = string.Empty;
    public string BloodType { get; set; } = string.Empty;
    public int Age { get; set; }
}