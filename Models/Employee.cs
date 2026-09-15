namespace FireflyHR.API.Models;

public class Employee
{
    public int Id { get; set; }
    public string EmployeeIdNumber { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Password { get; set; } = "firefly123";
    public bool MustChangePassword { get; set; } = true;
    public bool IsAdmin { get; set; } = false;
    public string MiddleName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public int Age { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string CivilStatus { get; set; } = string.Empty;
    public string CurrentAddress { get; set; } = string.Empty;
    public string PermanentAddress { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string PersonalEmailAddress { get; set; } = string.Empty;
    public string EmergencyContactName { get; set; } = string.Empty;
    public string EmergencyContactNumber { get; set; } = string.Empty;
    public string RelationToEmployee { get; set; } = string.Empty;
    public string EmergencyContactAddress { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string EmploymentType { get; set; } = "Regular"; // Regular, Probationary, OJT, Consultant
    public DateTime DateHired { get; set; }
    public DateTime DeclaredDateHired { get; set; }
    public string OfficeType { get; set; } = "Admin"; // Admin or Production
    public decimal DailySalary { get; set; }
    public decimal DailyAllowance { get; set; }
    public string BloodType { get; set; } = string.Empty;
    public bool HasGovernmentDeductions { get; set; } = false;
    public string SssNumber { get; set; } = string.Empty;
    public string PhilHealthNumber { get; set; } = string.Empty;
    public string PagIbigNumber { get; set; } = string.Empty;
    public string DeductionType { get; set; } = "Monthly 15"; // Per Pay Period, Monthly 15, Monthly 30
    public string? Photo { get; set; }
    public string EmploymentStatus { get; set; } = "Active"; // Active, Suspended, Resigned, Maternity
}