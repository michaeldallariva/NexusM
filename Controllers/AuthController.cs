using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexusM.Data;
using NexusM.Models;
using NexusM.Services;

namespace NexusM.Controllers;

/// <summary>
/// Authentication and user management API endpoints.
/// Handles PIN-based login, session management, and user CRUD (admin only).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UsersDbContext _db;
    private readonly PinSecurityService _pinSecurity;
    private readonly ConfigService _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UsersDbContext db,
        PinSecurityService pinSecurity,
        ConfigService config,
        ILogger<AuthController> logger)
    {
        _db = db;
        _pinSecurity = pinSecurity;
        _config = config;
        _logger = logger;
    }

    // ─── Login ──────────────────────────────────────────────────────

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Pin))
            return BadRequest(new { error = "Username and PIN are required" });

        var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Brute-force protection: check lockout
        var lockoutMinutes = _pinSecurity.GetLockoutMinutesRemaining(clientIP, dto.Username);
        if (lockoutMinutes > 0)
        {
            _logger.LogWarning("Login blocked for {Username} from {IP} - locked for {Minutes} more minutes",
                dto.Username, clientIP, lockoutMinutes);
            await Task.Delay(2000); // Slow down attacker
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { error = $"Too many failed attempts. Account locked for {lockoutMinutes} minutes." });
        }

        // Find active user
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == dto.Username && u.IsActive);

        if (user == null)
        {
            _pinSecurity.RecordFailedAttempt(clientIP, dto.Username);
            await Task.Delay(1000); // Slow down enumeration
            return Unauthorized(new { error = "Invalid username or PIN" });
        }

        // Verify PIN
        if (!_pinSecurity.VerifyPin(dto.Pin, user.PinHash, user.PinSalt))
        {
            var locked = _pinSecurity.RecordFailedAttempt(clientIP, dto.Username);
            await Task.Delay(1000); // Slow down brute force
            if (locked)
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = $"Too many failed attempts. Account locked for {PinSecurityService.LockoutMinutesPublic} minutes." });
            return Unauthorized(new { error = "Invalid username or PIN" });
        }

        // Successful login - clear failed attempts
        _pinSecurity.ClearFailedAttempts(clientIP, dto.Username);

        // Update last login
        user.LastLogin = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Create authentication cookie with claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("DisplayName", user.DisplayName ?? user.Username)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(_config.Config.Server.SessionTimeout)
            });

        _logger.LogInformation("User logged in: {Username} from {IP}", user.Username, clientIP);

        return Ok(new
        {
            userId = user.Id,
            username = user.Username,
            displayName = user.DisplayName ?? user.Username,
            role = user.Role,
            message = "Login successful"
        });
    }

    // ─── Logout ─────────────────────────────────────────────────────

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        var username = User.Identity?.Name ?? "unknown";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        _logger.LogInformation("User logged out: {Username}", username);
        return Ok(new { message = "Logged out" });
    }

    // ─── Session Info ───────────────────────────────────────────────

    [HttpGet("session")]
    [AllowAnonymous]
    public IActionResult GetSession()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Ok(new
            {
                authenticated = false,
                securityByPin = _config.Config.Security.SecurityByPin
            });
        }

        return Ok(new
        {
            authenticated = true,
            userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0"),
            username = User.Identity.Name,
            displayName = User.FindFirst("DisplayName")?.Value ?? User.Identity.Name,
            role = User.FindFirst(ClaimTypes.Role)?.Value ?? "guest",
            securityByPin = _config.Config.Security.SecurityByPin
        });
    }

    // ─── Public User List (for login page) ────────────────────────

    [HttpGet("users/public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUsersPublic()
    {
        var users = await _db.Users
            .Where(u => u.IsActive)
            .Select(u => new
            {
                u.Username,
                u.DisplayName,
                u.Role
            })
            .OrderBy(u => u.Username)
            .ToListAsync();

        return Ok(users);
    }

    // ─── List Users (admin only) ────────────────────────────────────

    [HttpGet("users")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.DisplayName,
                u.Role,
                u.IsActive,
                u.DateCreated,
                u.LastLogin
            })
            .OrderBy(u => u.Username)
            .ToListAsync();

        return Ok(users);
    }

    // ─── Create User (admin only) ───────────────────────────────────

    [HttpPost("users")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            return BadRequest(new { error = "Username is required" });

        if (string.IsNullOrWhiteSpace(dto.Pin) || !Regex.IsMatch(dto.Pin, @"^\d{6}$"))
            return BadRequest(new { error = "PIN must be exactly 6 digits" });

        var userType = dto.UserType?.ToLowerInvariant();
        if (userType is not ("admin" or "guest"))
            return BadRequest(new { error = "UserType must be 'admin' or 'guest'" });

        if (await _db.Users.AnyAsync(u => u.Username == dto.Username))
            return Conflict(new { error = $"Username '{dto.Username}' already exists" });

        var (hash, salt) = _pinSecurity.HashPin(dto.Pin);

        var user = new AppUser
        {
            Username = dto.Username,
            DisplayName = dto.DisplayName,
            PinHash = hash,
            PinSalt = salt,
            Role = userType,
            IsActive = true,
            DateCreated = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Create per-user database file
        _pinSecurity.CreateUserDatabase(dto.Username);

        _logger.LogInformation("User created: {Username} ({Role}) by {Admin}",
            user.Username, user.Role, User.Identity?.Name);

        return Ok(new
        {
            user.Id,
            user.Username,
            user.DisplayName,
            user.Role,
            message = "User created"
        });
    }

    // ─── Edit User (admin only) ─────────────────────────────────────

    [HttpPut("users/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> EditUser(int id, [FromBody] EditUserDto dto)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "User not found" });

        if (dto.DisplayName != null)
            user.DisplayName = dto.DisplayName;

        if (!string.IsNullOrWhiteSpace(dto.UserType))
        {
            var userType = dto.UserType.ToLowerInvariant();
            if (userType is not ("admin" or "guest"))
                return BadRequest(new { error = "UserType must be 'admin' or 'guest'" });
            user.Role = userType;
        }

        if (!string.IsNullOrWhiteSpace(dto.Pin))
        {
            if (!Regex.IsMatch(dto.Pin, @"^\d{6}$"))
                return BadRequest(new { error = "PIN must be exactly 6 digits" });

            var (hash, salt) = _pinSecurity.HashPin(dto.Pin);
            user.PinHash = hash;
            user.PinSalt = salt;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("User updated: {Username} by {Admin}", user.Username, User.Identity?.Name);

        return Ok(new
        {
            user.Id,
            user.Username,
            user.DisplayName,
            user.Role,
            message = "User updated"
        });
    }

    // ─── Delete User (admin only) ───────────────────────────────────

    [HttpDelete("users/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "User not found" });

        // Prevent self-deletion
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (currentUserId == id.ToString())
            return BadRequest(new { error = "Cannot delete your own account" });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        // Delete per-user database file
        var userDbPath = Path.Combine(AppContext.BaseDirectory, "users", $"{user.Username}.db");
        if (System.IO.File.Exists(userDbPath))
        {
            System.IO.File.Delete(userDbPath);
            _logger.LogInformation("Deleted user database: {Path}", userDbPath);
        }

        _logger.LogInformation("User deleted: {Username} by {Admin}", user.Username, User.Identity?.Name);

        return Ok(new { message = $"User '{user.Username}' deleted" });
    }
}

// ─── DTOs ────────────────────────────────────────────────────────────

public record LoginDto(string Username, string Pin);
public record CreateUserDto(string Username, string Pin, string UserType, string? DisplayName);
public record EditUserDto(string? DisplayName, string? UserType, string? Pin);
