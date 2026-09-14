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

    [Authorize]
    [HttpPut("profile/{id}")]
    public async Task<IActionResult> UpdateEmployeeProfile(int id, [FromBody] EmployeeUpdateDto updatedInfo)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee == null) return NotFound("Employee not found.");

        // Update only the fields allowed for employee self-service
        employee.CurrentAddress = updatedInfo.CurrentAddress;
        employee.ContactNumber = updatedInfo.ContactNumber;
        employee.PersonalEmailAddress = updatedInfo.PersonalEmailAddress;
        employee.EmergencyContactName = updatedInfo.EmergencyContactName;
        employee.EmergencyContactNumber = updatedInfo.EmergencyContactNumber;
        employee.RelationToEmployee = updatedInfo.RelationToEmployee;
        employee.EmergencyContactAddress = updatedInfo.EmergencyContactAddress;

        await _context.SaveChangesAsync();
        return Ok(new { Message = "Profile updated successfully.", employee });
    }
}