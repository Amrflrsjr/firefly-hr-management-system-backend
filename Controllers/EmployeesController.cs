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
    public async Task<ActionResult<IEnumerable<Employee>>> GetEmployees()
    {
        return await _context.Employees.ToListAsync();
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

    [Authorize]
    [HttpGet("{id}")]
    public async Task<ActionResult<Employee>> GetEmployee(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound("Employee not found.");
        return employee;
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