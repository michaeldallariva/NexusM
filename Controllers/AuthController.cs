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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NexusM.Controllers;

/// <summary>
/// Authentication and user management API endpoints.
/// Handles PIN-based login, session management, and user CRUD (admin only).
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // API tokens (Samsung TV, Google TV, mobile, Cast) live in Services/ApiTokenStore:
    // persisted (hashed) in data/apitokens.json so a restart or auto-update no longer logs
    // every TV out. Token format and the token endpoints are unchanged for clients.
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

    // ─── API Token (for native clients: Samsung TV, Android, etc.) ──────

    /// <summary>
    /// Issues a 30-day bearer token for API clients that cannot use cookies.
    /// Returns { token, username, role, expires }.
    /// Pass the token as Authorization: Bearer &lt;token&gt; header, or ?token=&lt;token&gt; query param.
    /// </summary>
    [HttpPost("token")]
    [AllowAnonymous]
    public async Task<IActionResult> GetApiToken([FromBody] ApiTokenRequestDto dto)
    {
        string username, role;
        bool pinSecurityEnabled = _config.Config.Security.SecurityByPin;

        if (!pinSecurityEnabled)
        {
            username = "admin";
            role = "admin";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto?.Username) || string.IsNullOrWhiteSpace(dto?.Pin))
                return BadRequest(new { error = "Username and PIN are required" });

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username && u.IsActive);
            if (user == null) { await Task.Delay(1000); return Unauthorized(new { error = "Invalid username or PIN" }); }

            if (!_pinSecurity.VerifyPin(dto.Pin, user.PinHash, user.PinSalt))
            {
                await Task.Delay(1000);
                return Unauthorized(new { error = "Invalid username or PIN" });
            }

            username = user.Username;
            role = user.Role ?? "guest";
        }

        // Same 32-hex token format and 30-day life as always; now persisted across restarts.
        var (token, expires) = ApiTokenStore.Issue(username, role, TimeSpan.FromDays(30), "client");

        _logger.LogInformation("API token issued for {Username}", username);
        return Ok(new { token, username, role, expires });
    }

    // ─── Cast Token (for the web UI's Google Cast feature) ─────────────

    /// <summary>
    /// Issues a 30-day bearer token for the already-authenticated session user.
    /// Used by the web UI to build tokenized stream URLs for Google Cast devices
    /// (which cannot send cookies). Unlike POST token, no username/PIN is required -
    /// the caller is already authenticated via the session cookie.
    /// </summary>
    [HttpPost("cast-token")]
    [Authorize]
    public IActionResult GetCastToken()
    {
        var username = User.Identity?.Name ?? "unknown";
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "guest";

        var (token, expires) = ApiTokenStore.Issue(username, role, TimeSpan.FromDays(30), "cast-web");
        return Ok(new { token, username, role, expires });
    }

    // ─── Revoke Token ────────────────────────────────────────────────

    [HttpDelete("token")]
    public IActionResult RevokeApiToken()
    {
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        var token = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? authHeader[7..] : Request.Query["token"].FirstOrDefault();
        if (!string.IsNullOrEmpty(token)) ApiTokenStore.Revoke(token);
        return Ok(new { message = "Token revoked" });
    }

    // ─── Login ──────────────────────────────────────────────────────

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        // When PIN security is disabled the server always logs in as the admin user -
        // no username or PIN is needed from the client.
        bool pinSecurityEnabled = _config.Config.Security.SecurityByPin;
        if (!pinSecurityEnabled)
            dto = dto with { Username = "admin", Pin = "" };

        if (string.IsNullOrWhiteSpace(dto.Username))
            return BadRequest(new { error = "Username is required" });

        // Demo mode: guest users may log in without a PIN
        bool isDemoGuestLogin = _config.Config.DemoMode.Enabled
                                && string.IsNullOrWhiteSpace(dto.Pin);

        if (!isDemoGuestLogin && pinSecurityEnabled && string.IsNullOrWhiteSpace(dto.Pin))
            return BadRequest(new { error = "Username and PIN are required" });

        var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Brute-force protection (not applicable to demo guest - no PIN to brute-force)
        if (!isDemoGuestLogin)
        {
            var lockoutMinutes = _pinSecurity.GetLockoutMinutesRemaining(clientIP, dto.Username);
            if (lockoutMinutes > 0)
            {
                _logger.LogWarning("Login blocked for {Username} from {IP} - locked for {Minutes} more minutes",
                    dto.Username, clientIP, lockoutMinutes);
                await Task.Delay(2000);
                return StatusCode(StatusCodes.Status429TooManyRequests,
                    new { error = $"Too many failed attempts. Account locked for {lockoutMinutes} minutes." });
            }
        }

        // Find active user
        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.Username == dto.Username && u.IsActive);

        if (user == null)
        {
            if (!isDemoGuestLogin) _pinSecurity.RecordFailedAttempt(clientIP, dto.Username);
            await Task.Delay(1000);
            return Unauthorized(new { error = "Invalid username or PIN" });
        }

        if (isDemoGuestLogin)
        {
            // Demo mode PIN bypass is only permitted for the guest role
            if (user.Role != "guest")
            {
                _logger.LogWarning("Demo mode PIN bypass denied for non-guest user: {Username}", dto.Username);
                return Unauthorized(new { error = "PIN required" });
            }
            // PIN verification skipped - guest auto-login allowed in demo mode
        }
        else if (pinSecurityEnabled)
        {
            // Normal PIN verification (skipped when SecurityByPin is disabled)
            if (!_pinSecurity.VerifyPin(dto.Pin, user.PinHash, user.PinSalt))
            {
                var locked = _pinSecurity.RecordFailedAttempt(clientIP, dto.Username);
                await Task.Delay(1000);
                if (locked)
                    return StatusCode(StatusCodes.Status429TooManyRequests,
                        new { error = $"Too many failed attempts. Account locked for {PinSecurityService.LockoutMinutesPublic} minutes." });
                return Unauthorized(new { error = "Invalid username or PIN" });
            }
            // Successful PIN - clear failed attempts
            _pinSecurity.ClearFailedAttempts(clientIP, dto.Username);
        }

        // Viewing hours restriction for child users
        if (user.Role == "child" && !string.IsNullOrEmpty(user.ChildSettings))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(user.ChildSettings);
                var cs = doc.RootElement;
                if (cs.TryGetProperty("viewingHoursEnabled", out var vhEnabled) && vhEnabled.GetBoolean())
                {
                    var startStr = cs.TryGetProperty("viewingHoursStart", out var s) ? s.GetString() : null;
                    var endStr   = cs.TryGetProperty("viewingHoursEnd",   out var e) ? e.GetString() : null;
                    if (!string.IsNullOrEmpty(startStr) && !string.IsNullOrEmpty(endStr) &&
                        TimeSpan.TryParse(startStr, out var startTime) &&
                        TimeSpan.TryParse(endStr,   out var endTime))
                    {
                        var now = DateTime.Now.TimeOfDay;
                        bool inWindow = startTime <= endTime
                            ? now >= startTime && now <= endTime
                            : now >= startTime || now <= endTime; // overnight range
                        if (!inWindow)
                            return Unauthorized(new
                            {
                                error = $"Viewing not available right now. Allowed hours: {startStr}–{endStr}."
                            });
                    }
                }
            }
            catch { /* malformed JSON - allow login */ }
        }

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
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(_config.Config.Server.SessionTimeout)
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

    // ─── Public config (mobile clients check this before showing login UI) ───

    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult GetAuthConfig()
    {
        return Ok(new { pinRequired = _config.Config.Security.SecurityByPin });
    }

    // ─── Session Info ───────────────────────────────────────────────

    [HttpGet("session")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSession()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Ok(new
            {
                authenticated = false,
                securityByPin = _config.Config.Security.SecurityByPin,
                demoModeEnabled = _config.Config.DemoMode.Enabled,
                language = _config.Config.UI.Language
            });
        }

        var currentUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == User.Identity!.Name);

        return Ok(new
        {
            authenticated = true,
            userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0"),
            username = User.Identity.Name,
            displayName = User.FindFirst("DisplayName")?.Value ?? User.Identity.Name,
            role = User.FindFirst(ClaimTypes.Role)?.Value ?? "guest",
            securityByPin = _config.Config.Security.SecurityByPin,
            childSettings = currentUser?.ChildSettings   // null for admin/guest
        });
    }

    // ─── Public User List (for login page) ────────────────────────

    [HttpGet("users/public")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUsersPublic()
    {
        // Load raw rows (need ChildSettings for viewing-hours extraction)
        var raw = await _db.Users
            .Where(u => u.IsActive)
            .Select(u => new { u.Username, u.DisplayName, u.Role, u.ChildSettings })
            .OrderBy(u => u.Username)
            .ToListAsync();

        // Extract viewing-hours from ChildSettings JSON (child users only)
        var users = raw.Select(u =>
        {
            bool viewingHoursEnabled = false;
            string? viewingHoursStart = null;
            string? viewingHoursEnd   = null;

            if (u.Role == "child" && !string.IsNullOrEmpty(u.ChildSettings))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(u.ChildSettings);
                    var cs = doc.RootElement;
                    if (cs.TryGetProperty("viewingHoursEnabled", out var vhe))
                        viewingHoursEnabled = vhe.GetBoolean();
                    if (cs.TryGetProperty("viewingHoursStart", out var vhs))
                        viewingHoursStart = vhs.GetString();
                    if (cs.TryGetProperty("viewingHoursEnd", out var vhe2))
                        viewingHoursEnd = vhe2.GetString();
                }
                catch { /* malformed JSON - treat as no restriction */ }
            }

            return new
            {
                u.Username,
                u.DisplayName,
                u.Role,
                viewingHoursEnabled,
                viewingHoursStart,
                viewingHoursEnd,
                hasPicture = System.IO.File.Exists(GetProfilePicturePath(u.Username))
            };
        }).ToList();

        return Ok(users);
    }

    // ─── List Users (admin only) ────────────────────────────────────

    [HttpGet("users")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetUsers()
    {
        var rawUsers = await _db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.DisplayName,
                u.Role,
                u.IsActive,
                u.DateCreated,
                u.LastLogin,
                u.ChildSettings
            })
            .OrderBy(u => u.Username)
            .ToListAsync();

        var users = rawUsers.Select(u => new
        {
            u.Id,
            u.Username,
            u.DisplayName,
            u.Role,
            u.IsActive,
            u.DateCreated,
            u.LastLogin,
            u.ChildSettings,
            hasPicture = System.IO.File.Exists(GetProfilePicturePath(u.Username))
        }).ToList();

        return Ok(users);
    }

    // ─── Create User (admin only) ───────────────────────────────────

    [HttpPost("users")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            return BadRequest(new { error = "Username is required" });

        // The name becomes users/{name}.db and assets/profile/{name}.jpg, so it must be a plain
        // file name: no separators, "..", reserved device names or invalid characters.
        var username = dto.Username.Trim();
        if (!UserNames.IsValidNew(username))
            return BadRequest(new { error = UserNames.RuleText });
        dto = dto with { Username = username };

        if (string.IsNullOrWhiteSpace(dto.Pin) || !Regex.IsMatch(dto.Pin, @"^\d{6}$"))
            return BadRequest(new { error = "PIN must be exactly 6 digits" });

        var userType = dto.UserType?.ToLowerInvariant();
        if (userType is not ("admin" or "guest" or "child"))
            return BadRequest(new { error = "UserType must be 'admin', 'guest', or 'child'" });

        // Case-insensitive: "Bob" and "bob" would share one database file on Windows.
        var existing = await _db.Users.Select(u => u.Username).ToListAsync();
        if (existing.Any(n => string.Equals(n, username, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { error = $"Username '{username}' already exists" });

        var (hash, salt) = _pinSecurity.HashPin(dto.Pin);

        var user = new AppUser
        {
            Username = dto.Username,
            DisplayName = dto.DisplayName,
            PinHash = hash,
            PinSalt = salt,
            Role = userType,
            IsActive = true,
            DateCreated = DateTime.UtcNow,
            ChildSettings = (userType == "child" || userType == "guest") ? dto.ChildSettings : null
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        AutoLoginCache.Invalidate();

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
            if (userType is not ("admin" or "guest" or "child"))
                return BadRequest(new { error = "UserType must be 'admin', 'guest', or 'child'" });
            user.Role = userType;
            ApiTokenStore.UpdateRole(user.Username, userType);   // TV/mobile tokens carry the role
        }

        if (!string.IsNullOrWhiteSpace(dto.Pin))
        {
            if (!Regex.IsMatch(dto.Pin, @"^\d{6}$"))
                return BadRequest(new { error = "PIN must be exactly 6 digits" });

            var (hash, salt) = _pinSecurity.HashPin(dto.Pin);
            user.PinHash = hash;
            user.PinSalt = salt;
        }

        // Child settings: empty string clears; non-null/non-empty updates
        if (dto.ChildSettings != null)
            user.ChildSettings = string.IsNullOrEmpty(dto.ChildSettings) ? null : dto.ChildSettings;

        await _db.SaveChangesAsync();
        AutoLoginCache.Invalidate();

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

        // Files first, row second: if the database file cannot be removed nothing changes and
        // the admin gets a clear error, instead of an orphaned file next to a vanished account.
        // Microsoft.Data.Sqlite pools connections, and on Windows a pooled handle keeps the file
        // locked, so the pool is cleared before the delete (with a short retry for in-flight use).
        if (UserNames.IsPathSafe(user.Username))
        {
            var userDbPath = UserNames.DbPath(user.Username);
            var existed = System.IO.File.Exists(userDbPath);
            if (!UserNames.TryDeleteDb(userDbPath, out var delErr))
            {
                _logger.LogError(delErr, "Could not delete user database {Path}", userDbPath);
                return StatusCode(500, new { error = "The user's database file is still in use. Try again in a moment." });
            }
            if (existed) _logger.LogInformation("Deleted user database: {Path}", userDbPath);

            var picPath = UserNames.ProfilePicturePath(user.Username);
            if (System.IO.File.Exists(picPath))
                System.IO.File.Delete(picPath);
        }
        else
        {
            // A legacy name that is not a valid file name never had files of its own.
            _logger.LogWarning("User '{Username}' has an unsafe name; no files removed", user.Username);
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        AutoLoginCache.Invalidate();
        ApiTokenStore.RevokeUser(user.Username);   // a deleted account must not keep a live TV token

        _logger.LogInformation("User deleted: {Username} by {Admin}", user.Username, User.Identity?.Name);

        return Ok(new { message = $"User '{user.Username}' deleted" });
    }

    // ─── Change Own PIN (any signed-in user) ─────────────────────────

    /// <summary>
    /// Lets the signed-in user change their OWN PIN. Unlike EditUser (admin-only,
    /// sets a PIN without knowing the old one) this requires the current PIN, so a
    /// borrowed/unattended session cannot silently lock the real owner out.
    /// </summary>
    [HttpPost("change-pin")]
    [Authorize]
    public async Task<IActionResult> ChangeOwnPin([FromBody] ChangePinDto dto)
    {
        // When PIN security is off every session is auto-authenticated as admin
        // (see the auto-login middleware in Program.cs) - there is no credential to
        // verify against, so allowing a change here would be meaningless.
        if (!_config.Config.Security.SecurityByPin)
            return BadRequest(new { error = "PIN security is disabled" });

        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return NotFound(new { error = "User not found" });

        if (!Regex.IsMatch(dto.NewPin ?? "", @"^\d{6}$"))
            return BadRequest(new { error = "PIN must be exactly 6 digits" });

        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Reuse the login lockout so this endpoint can't be used as an oracle to
        // brute-force the current PIN past the 5-attempt limit.
        var lockedFor = _pinSecurity.GetLockoutMinutesRemaining(clientIp, username);
        if (lockedFor > 0)
            return StatusCode(429, new { error = $"Too many failed attempts. Try again in {lockedFor} minute(s)." });

        if (!_pinSecurity.VerifyPin(dto.CurrentPin ?? "", user.PinHash, user.PinSalt))
        {
            _pinSecurity.RecordFailedAttempt(clientIp, username);
            await Task.Delay(1000);
            _logger.LogWarning("Failed PIN change (wrong current PIN) for {Username} from {IP}", username, clientIp);
            return BadRequest(new { error = "Current PIN is incorrect" });
        }

        _pinSecurity.ClearFailedAttempts(clientIp, username);

        var (hash, salt) = _pinSecurity.HashPin(dto.NewPin!);
        user.PinHash = hash;
        user.PinSalt = salt;
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {Username} changed their own PIN", username);
        return Ok(new { message = "PIN changed" });
    }

    // ─── Get Profile Picture ─────────────────────────────────────────

    [HttpGet("users/{username}/profile-picture")]
    [AllowAnonymous]
    public IActionResult GetProfilePicture(string username)
    {
        if (!UserNames.IsPathSafe(username)) return NotFound();
        var path = GetProfilePicturePath(username);
        if (!System.IO.File.Exists(path))
            return NotFound();
        return PhysicalFile(path, "image/jpeg");
    }

    // ─── Upload Profile Picture ──────────────────────────────────────

    [HttpPost("users/{username}/profile-picture")]
    [Authorize]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadProfilePicture(string username, IFormFile? file)
    {
        if (!User.IsInRole("admin") && !string.Equals(User.Identity?.Name, username, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });
        if (!UserNames.IsPathSafe(username))
            return BadRequest(new { error = "Invalid username" });

        var dir = Path.Combine(AppContext.BaseDirectory, "assets", "profile");
        Directory.CreateDirectory(dir);
        var dest = GetProfilePicturePath(username);

        using var img = await Image.LoadAsync(file.OpenReadStream());
        img.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(150, 150),
            Mode = ResizeMode.Crop
        }));
        await img.SaveAsJpegAsync(dest);

        return Ok(new { message = "Profile picture saved" });
    }

    // ─── Delete Profile Picture ──────────────────────────────────────

    [HttpDelete("users/{username}/profile-picture")]
    [Authorize]
    public IActionResult DeleteProfilePicture(string username)
    {
        if (!User.IsInRole("admin") && !string.Equals(User.Identity?.Name, username, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        if (!UserNames.IsPathSafe(username))
            return BadRequest(new { error = "Invalid username" });
        var path = GetProfilePicturePath(username);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);

        return Ok(new { message = "Profile picture deleted" });
    }

    // Callers must check UserNames.IsPathSafe first; an unsafe name maps to a path that can
    // never exist, so lookups simply miss instead of escaping the profile folder.
    private static string GetProfilePicturePath(string username) =>
        UserNames.IsPathSafe(username)
            ? UserNames.ProfilePicturePath(username)
            : Path.Combine(AppContext.BaseDirectory, "assets", "profile", "__invalid__.jpg");
}

// ─── DTOs ────────────────────────────────────────────────────────────

public record LoginDto(string Username, string Pin);
public record ApiTokenRequestDto(string? Username, string? Pin);
public record CreateUserDto(string Username, string Pin, string UserType, string? DisplayName, string? ChildSettings);
public record ChangePinDto(string? CurrentPin, string? NewPin);
public record EditUserDto(string? DisplayName, string? UserType, string? Pin, string? ChildSettings);
