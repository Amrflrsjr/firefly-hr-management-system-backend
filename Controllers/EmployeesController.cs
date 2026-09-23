using FireflyHR.API.Data;
using FireflyHR.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FireflyHR.API.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmployeesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetEmployees()
    {
        var employees = await _context.Employees
            .Where(e => !e.IsAdmin)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync();

        var leaves = await _context.Leaves
            .Where(l => l.Status == "Approved" || l.Status == "In Review")
            .ToListAsync();

        var result = employees.Select(e =>
        {
            // Only count leaves that were created AFTER the employee's last reset date (if any)
            var relevantLeaves = leaves.Where(l => l.EmployeeId == e.Id);
            if (e.LastLeaveResetDate.HasValue)
            {
                relevantLeaves = relevantLeaves.Where(l => l.LeaveDate >= e.LastLeaveResetDate.Value);
            }

            decimal usedHours = relevantLeaves.Sum(l => l.LeaveHours);
            decimal remainingHours = Math.Max(0, e.MaxLeaveHours - usedHours);

            return new
            {
                e.Id,
                e.EmployeeIdNumber,
                e.Username,
                e.FirstName,
                e.LastName,
                e.MiddleName,
                e.Password,
                e.MustChangePassword,
                e.IsAdmin,
                e.DateOfBirth,
                e.Age,
                e.Gender,
                e.CivilStatus,
                e.CurrentAddress,
                e.PermanentAddress,
                e.ContactNumber,
                e.PersonalEmailAddress,
                e.EmergencyContactName,
                e.EmergencyContactNumber,
                e.RelationToEmployee,
                e.EmergencyContactAddress,
                e.JobTitle,
                e.EmploymentType,
                e.DateHired,
                e.DeclaredDateHired,
                e.OfficeType,
                e.DailySalary,
                e.DailyAllowance,
                e.BloodType,
                e.HasGovernmentDeductions,
                e.SssNumber,
                e.PhilHealthNumber,
                e.PagIbigNumber,
                e.DeductionType,
                e.Photo,
                e.EmploymentStatus,
                e.MaxLeaveHours,
                UsedLeaveHours = usedHours,
                RemainingLeaveHours = remainingHours
            };
        });

        return Ok(result);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<ActionResult<Employee>> PostEmployee(Employee employee)
    {
        // Force DateTime to UTC to prevent Npgsql timestamp timezone mismatch
        if (employee.DateOfBirth != default)
            employee.DateOfBirth = DateTime.SpecifyKind(employee.DateOfBirth, DateTimeKind.Utc);

        if (employee.DateHired != default)
            employee.DateHired = DateTime.SpecifyKind(employee.DateHired, DateTimeKind.Utc);

        if (employee.DeclaredDateHired != default)
            employee.DeclaredDateHired = DateTime.SpecifyKind(employee.DeclaredDateHired, DateTimeKind.Utc);

        // Enforce 40 default limit if not provided
        if (employee.MaxLeaveHours <= 0)
        {
            employee.MaxLeaveHours = 40.0m;
        }

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetEmployees), new { id = employee.Id }, employee);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteEmployee(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound();
        _context.Employees.Remove(employee);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/reset-leave-balance")]
    public async Task<IActionResult> ResetLeaveBalance(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound("Employee not found.");

        // Set the reset timestamp to now so prior leaves are ignored in balance calculations
        employee.LastLeaveResetDate = DateTime.UtcNow;

        _context.Notifications.Add(new Notification
        {
            EmployeeId = id,
            Title = "Leave Balance Reset",
            Message = $"Your leave balance has been reset by an administrator. You now have your full {employee.MaxLeaveHours} hours available.",
            Type = "Leave"
        });

        await _context.SaveChangesAsync();

        return Ok(new { message = $"Leave balance for {employee.FirstName} {employee.LastName} has been successfully reset." });
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetEmployee(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound("Employee not found.");

        var leaves = await _context.Leaves
            .Where(l => l.EmployeeId == id && (l.Status == "Approved" || l.Status == "In Review"))
            .ToListAsync();

        var relevantLeaves = leaves.AsEnumerable();
        if (employee.LastLeaveResetDate.HasValue)
        {
            relevantLeaves = relevantLeaves.Where(l => l.LeaveDate >= employee.LastLeaveResetDate.Value);
        }

        decimal usedHours = relevantLeaves.Sum(l => l.LeaveHours);
        decimal remainingHours = Math.Max(0, employee.MaxLeaveHours - usedHours);

        var response = new
        {
            employee.Id,
            employee.EmployeeIdNumber,
            employee.Username,
            employee.FirstName,
            employee.LastName,
            employee.MiddleName,
            employee.Password,
            employee.MustChangePassword,
            employee.IsAdmin,
            employee.DateOfBirth,
            employee.Age,
            employee.Gender,
            employee.CivilStatus,
            employee.CurrentAddress,
            employee.PermanentAddress,
            employee.ContactNumber,
            employee.PersonalEmailAddress,
            employee.EmergencyContactName,
            employee.EmergencyContactNumber,
            employee.RelationToEmployee,
            employee.EmergencyContactAddress,
            employee.JobTitle,
            employee.EmploymentType,
            employee.DateHired,
            employee.DeclaredDateHired,
            employee.OfficeType,
            employee.DailySalary,
            employee.DailyAllowance,
            employee.BloodType,
            employee.HasGovernmentDeductions,
            employee.SssNumber,
            employee.PhilHealthNumber,
            employee.PagIbigNumber,
            employee.DeductionType,
            employee.Photo,
            employee.EmploymentStatus,
            employee.MaxLeaveHours,
            UsedLeaveHours = usedHours,
            RemainingLeaveHours = remainingHours
        };

        return Ok(response);
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> PutEmployee(int id, Employee updatedEmployee)
    {
        if (id != updatedEmployee.Id)
        {
            return BadRequest("Employee ID mismatch.");
        }

        var employee = await _context.Employees.FindAsync(id);
        if (employee == null)
        {
            return NotFound("Employee not found.");
        }

        // Update admin-controlled fields
        employee.EmployeeIdNumber = updatedEmployee.EmployeeIdNumber;
        employee.Username = updatedEmployee.Username;
        employee.FirstName = updatedEmployee.FirstName;
        employee.LastName = updatedEmployee.LastName;
        employee.MiddleName = updatedEmployee.MiddleName;
        employee.JobTitle = updatedEmployee.JobTitle;
        employee.EmploymentType = updatedEmployee.EmploymentType;
        employee.OfficeType = updatedEmployee.OfficeType;
        employee.DailySalary = updatedEmployee.DailySalary;
        employee.DailyAllowance = updatedEmployee.DailyAllowance;
        employee.HasGovernmentDeductions = updatedEmployee.HasGovernmentDeductions;
        employee.DeductionType = updatedEmployee.DeductionType;
        employee.IsAdmin = updatedEmployee.IsAdmin;
        employee.MaxLeaveHours = updatedEmployee.MaxLeaveHours;

        // Statutory numbers
        employee.SssNumber = updatedEmployee.SssNumber;
        employee.PhilHealthNumber = updatedEmployee.PhilHealthNumber;
        employee.PagIbigNumber = updatedEmployee.PagIbigNumber;

        // Personal details mapping fix
        employee.Gender = updatedEmployee.Gender;
        employee.CivilStatus = updatedEmployee.CivilStatus;
        employee.BloodType = updatedEmployee.BloodType;
        employee.Age = updatedEmployee.Age;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.Employees.Any(e => e.Id == id))
            {
                return NotFound("Employee not found.");
            }
            throw;
        }

        return Ok(new { message = "Employee updated successfully.", employee });
    }

    [Authorize]
    [HttpPut("profile/{id}")]
    public async Task<IActionResult> UpdateEmployeeProfile(int id, [FromBody] EmployeeUpdateDto updatedInfo)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound("Employee not found.");

        // Update self-service profile fields including Username
        employee.Username = updatedInfo.Username;
        employee.CurrentAddress = updatedInfo.CurrentAddress;
        employee.PermanentAddress = updatedInfo.PermanentAddress;
        employee.ContactNumber = updatedInfo.ContactNumber;
        employee.PersonalEmailAddress = updatedInfo.PersonalEmailAddress;
        employee.EmergencyContactName = updatedInfo.EmergencyContactName;
        employee.EmergencyContactNumber = updatedInfo.EmergencyContactNumber;
        employee.RelationToEmployee = updatedInfo.RelationToEmployee;
        employee.EmergencyContactAddress = updatedInfo.EmergencyContactAddress;

        employee.SssNumber = updatedInfo.SssNumber;
        employee.PhilHealthNumber = updatedInfo.PhilHealthNumber;
        employee.PagIbigNumber = updatedInfo.PagIbigNumber;

        employee.Gender = updatedInfo.Gender;
        employee.CivilStatus = updatedInfo.CivilStatus;
        employee.BloodType = updatedInfo.BloodType;
        employee.Age = updatedInfo.Age;

        await _context.SaveChangesAsync();
        return Ok(new { Message = "Profile updated successfully.", employee });
    }
}