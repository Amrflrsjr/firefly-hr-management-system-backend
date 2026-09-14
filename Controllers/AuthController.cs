using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FireflyHR.API.Data;
using FireflyHR.API.Models;

namespace FireflyHR.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _context;

    // Static variable to preserve the admin password dynamically at runtime
    private static string _currentAdminPassword = "firefly2026";

    public AuthController(IConfiguration configuration, AppDbContext context)
    {
        _configuration = configuration;
        _context = context;
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] UserLoginDto request)
    {
        // 1. Check Hardcoded Super Admin Login
        if (request.Username == "admin" && request.Password == _currentAdminPassword)
        {
            var adminToken = GenerateJwtToken(request.Username, "Admin");
            return Ok(new
            {
                Token = adminToken,
                Role = "Admin",
                UserName = "Administrator",
                MustChangePassword = false
            });
        }

        // 2. Check Database Employees (including Admin-flagged employees) against Username
        var employee = _context.Employees.FirstOrDefault(e =>
            e.Username == request.Username && e.Password == request.Password);

        if (employee != null)
        {
            var userRole = employee.IsAdmin ? "Admin" : "Employee";
            var employeeToken = GenerateJwtToken(request.Username, userRole);
            return Ok(new
            {
                Token = employeeToken,
                Role = userRole,
                EmployeeId = employee.Id,
                UserName = $"{employee.FirstName} {employee.LastName}",
                MustChangePassword = employee.MustChangePassword
            });
        }

        return Unauthorized("Invalid username or password.");
    }

    [HttpPost("change-password")]
    public IActionResult ChangePassword([FromBody] UserPasswordUpdateDto request)
    {
        // 1. Handle Hardcoded Super Admin Password Update
        if (request.Username == "admin")
        {
            if (request.CurrentPassword != _currentAdminPassword)
                return BadRequest("Current password is incorrect.");

            _currentAdminPassword = request.NewPassword;
            return Ok(new { Message = "Admin password updated successfully." });
        }

        // 2. Handle Database User Password Update (Employees & Admin Employees) using Username
        var employee = _context.Employees.FirstOrDefault(e => e.Username == request.Username);
        if (employee == null)
            return NotFound("User not found.");

        if (employee.Password != request.CurrentPassword)
            return BadRequest("Current password is incorrect.");

        employee.Password = request.NewPassword;
        employee.MustChangePassword = false;
        _context.SaveChanges();

        return Ok(new { Message = "Password updated successfully." });
    }

    [HttpPost("update-username")]
    public IActionResult UpdateUsername([FromBody] UsernameUpdateDto request)
    {
        var employee = _context.Employees.FirstOrDefault(e => e.Id == request.EmployeeId);
        if (employee == null)
            return NotFound("User not found.");

        // Check if username is already taken by another employee
        if (_context.Employees.Any(e => e.Username == request.NewUsername && e.Id != request.EmployeeId))
            return BadRequest("Username is already taken.");

        employee.Username = request.NewUsername;
        _context.SaveChanges();

        return Ok(new { Message = "Username updated successfully." });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        return Ok(new { Message = "Logged out successfully." });
    }

    private string GenerateJwtToken(string username, string role)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var key = Encoding.UTF8.GetBytes(jwtSettings["Key"]!);

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(1),
            Issuer = jwtSettings["Issuer"],
            Audience = jwtSettings["Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}