using System.Net;
using NexusM.Services;

namespace NexusM.Middleware;

/// <summary>
/// Middleware that restricts access based on IP whitelist from NexusM.conf.
/// If the whitelist is empty, all IPs are allowed.
/// Mirrors the IP whitelist behavior from the PowerShell NexusM version.
/// </summary>
public class IPWhitelistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IPWhitelistMiddleware> _logger;
    private readonly HashSet<string> _allowedIPs;
    private readonly bool _isEnabled;

    public IPWhitelistMiddleware(
        RequestDelegate next,
        ILogger<IPWhitelistMiddleware> logger,
        ConfigService configService)
    {
        _next = next;
        _logger = logger;

        var whitelist = configService.Config.Security.GetIPWhitelistList();
        _allowedIPs = new HashSet<string>(whitelist, StringComparer.OrdinalIgnoreCase);
        _isEnabled = _allowedIPs.Count > 0;

        if (_isEnabled)
            _logger.LogInformation("IP whitelist enabled with {Count} IPs: {IPs}", _allowedIPs.Count, string.Join(", ", _allowedIPs));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_isEnabled)
        {
            await _next(context);
            return;
        }

        var remoteIP = context.Connection.RemoteIpAddress;
        if (remoteIP == null)
        {
            _logger.LogWarning("Request with no remote IP address - blocked");
            await WriteDeniedResponse(context, "unknown");
            return;
        }

        var ipString = NormalizeIP(remoteIP);

        // Always allow localhost
        if (IsLocalhost(ipString))
        {
            await _next(context);
            return;
        }

        if (_allowedIPs.Contains(ipString))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning("IP blocked: {IP} (not in whitelist)", ipString);
        await WriteDeniedResponse(context, ipString);
    }

    private static async Task WriteDeniedResponse(HttpContext context, string clientIP)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        // Return JSON for API requests, HTML for browser requests
        var accept = context.Request.Headers.Accept.ToString();
        if (accept.Contains("application/json") && !accept.Contains("text/html"))
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"error\":\"Access denied - IP not in whitelist\"}");
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(GetDeniedPageHtml(clientIP));
    }

    private static string GetDeniedPageHtml(string clientIP)
    {
        var safeIP = System.Net.WebUtility.HtmlEncode(clientIP);
        return "<!DOCTYPE html>" +
            "<html lang=\"en\"><head><meta charset=\"UTF-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">" +
            "<title>Access Denied - NexusM</title><style>" +
            "*{margin:0;padding:0;box-sizing:border-box}" +
            "body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;" +
            "background:#121212;color:#e0e0e0;display:flex;align-items:center;justify-content:center;min-height:100vh;padding:20px}" +
            ".denied-box{text-align:center;max-width:480px}" +
            ".shield{width:80px;height:80px;background:rgba(231,76,60,0.12);border:2px solid rgba(231,76,60,0.3);" +
            "border-radius:50%;display:flex;align-items:center;justify-content:center;margin:0 auto 24px;font-size:36px}" +
            "h1{font-size:28px;font-weight:600;margin-bottom:12px}" +
            ".message{font-size:15px;color:#999;line-height:1.6;margin-bottom:24px}" +
            ".ip-display{display:inline-block;background:#1e1e1e;border:1px solid #333;border-radius:6px;" +
            "padding:8px 16px;font-family:Consolas,Monaco,monospace;font-size:14px;color:#e74c3c;margin-bottom:32px}" +
            ".footer-text{font-size:12px;color:#666}" +
            "</style></head><body><div class=\"denied-box\">" +
            "<div class=\"shield\">&#128274;</div>" +
            "<h1>Access Denied</h1>" +
            "<p class=\"message\">Your IP address is not in the authorized whitelist.<br>" +
            "Contact the server administrator to request access.</p>" +
            "<div class=\"ip-display\">Your IP: " + safeIP + "</div>" +
            "<p class=\"footer-text\">NexusM - IP Whitelist Protection</p>" +
            "</div></body></html>";
    }

    /// <summary>
    /// Normalize an IP address: handle IPv4-mapped IPv6 (::ffff:x.x.x.x) and brackets.
    /// </summary>
    private static string NormalizeIP(IPAddress address)
    {
        // Handle IPv4-mapped IPv6 addresses (e.g., ::ffff:192.168.1.1 → 192.168.1.1)
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.ToString().Trim('[', ']');
    }

    private static bool IsLocalhost(string ip)
    {
        return ip is "127.0.0.1" or "::1" or "0.0.0.1";
    }
}
