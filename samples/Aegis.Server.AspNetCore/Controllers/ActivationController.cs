using Aegis.Enums;
using Aegis.Exceptions;
using Aegis.Server.AspNetCore.Attributes;
using Aegis.Server.AspNetCore.Data.Context;
using Aegis.Server.AspNetCore.DTOs.Activation;
using Aegis.Server.AspNetCore.Services;
using Aegis.Server.Exceptions;
using Aegis.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Server.AspNetCore.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ActivationController(
    LicenseService licenseService,
    ApplicationDbContext dbContext,
    ClientSessionTokenService sessionTokenService) : ControllerBase
{
    private static readonly TimeSpan SessionTokenValidity = TimeSpan.FromMinutes(2);
    private const int HeartbeatIntervalSec = 30;

    [HttpPost("activate")]
    [RateLimitingMiddleware(5, "00:01:00")]
    public async Task<IActionResult> Activate([FromBody] ActivationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.HardwareId))
            return BadRequest(new ActivationResponse
            {
                Success = false,
                ErrorCode = "INVALID_REQUEST",
                ErrorMessage = "LicenseKey and HardwareId are required."
            });

        var licenseKey = request.LicenseKey.Trim().ToUpperInvariant();
        var hardwareId = request.HardwareId.Trim();

        // 1. Validate license existence & basic status
        var validation = await licenseService.ValidateLicenseAsync(licenseKey, null);
        if (!validation.IsValid)
            return Ok(BuildFailure(MapValidationException(validation.Exception)));

        var license = validation.License!;

        // 2. Only Concurrent licenses are supported for online client activation
        if (license.Type != LicenseType.Concurrent)
            return Ok(new ActivationResponse
            {
                Success = false,
                ErrorCode = "INVALID_LICENSE_TYPE",
                ErrorMessage = "此卡密类型不支持联网激活。"
            });

        // 3. Apply LicenseExtension (first-activation-starts-clock)
        var extension = await dbContext.LicenseExtensions
            .FirstOrDefaultAsync(le => le.LicenseId == license.LicenseId);

        if (extension != null && extension.FirstActivatedAt == null)
        {
            var now = DateTime.UtcNow;
            extension.FirstActivatedAt = now;
            license.ExpirationDate = now.AddDays(extension.ValidityDays);
            dbContext.LicenseExtensions.Update(extension);
            dbContext.Licenses.Update(license);
            await dbContext.SaveChangesAsync();
        }

        // 4. Re-connect path: same hardware already holds a slot → just refresh heartbeat
        var existing = await dbContext.Activations
            .FirstOrDefaultAsync(a => a.License.LicenseKey == licenseKey && a.MachineId == hardwareId);

        if (existing != null)
        {
            existing.LastHeartbeat = DateTime.UtcNow;
            dbContext.Activations.Update(existing);
            await dbContext.SaveChangesAsync();
        }
        else
        {
            // 5. New slot via Aegis LicenseService (handles concurrent capacity check)
            var activationResult = await licenseService.ActivateLicenseAsync(licenseKey, hardwareId);
            if (!activationResult.IsSuccessful)
                return Ok(BuildFailure(MapActivationException(activationResult.Exception)));
        }

        // 6. Issue session token
        var (token, expiry) = sessionTokenService.Issue(licenseKey, hardwareId, SessionTokenValidity);

        return Ok(new ActivationResponse
        {
            Success = true,
            SessionToken = token,
            ExpiryUtc = expiry,
            HeartbeatIntervalSec = HeartbeatIntervalSec,
            LicenseExpiryUtc = license.ExpirationDate
        });
    }

    [HttpPost("heartbeat")]
    [RateLimitingMiddleware(60, "00:01:00")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequestDto request)
    {
        var session = sessionTokenService.Resolve(request.SessionToken);
        if (session == null)
            return StatusCode(StatusCodes.Status401Unauthorized, new HeartbeatResponse
            {
                Valid = false,
                ErrorCode = "INVALID_TOKEN",
                ErrorMessage = "会话令牌无效或已过期，请重新激活。"
            });

        // Check current license status (admin may have revoked / it may have expired since activation)
        var license = await dbContext.Licenses
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LicenseKey == session.LicenseKey);

        if (license == null)
        {
            sessionTokenService.Revoke(request.SessionToken);
            return StatusCode(StatusCodes.Status410Gone, new HeartbeatResponse
            {
                Valid = false,
                ErrorCode = "LICENSE_NOT_FOUND",
                ErrorMessage = "卡密不存在。"
            });
        }

        if (license.Status == LicenseStatus.Revoked)
        {
            await licenseService.DisconnectConcurrentLicenseUser(session.LicenseKey, session.HardwareId);
            sessionTokenService.Revoke(request.SessionToken);
            return StatusCode(StatusCodes.Status410Gone, new HeartbeatResponse
            {
                Valid = false,
                ErrorCode = "LICENSE_REVOKED",
                ErrorMessage = "卡密已被吊销。"
            });
        }

        if (license.ExpirationDate.HasValue && license.ExpirationDate.Value < DateTime.UtcNow)
        {
            await licenseService.DisconnectConcurrentLicenseUser(session.LicenseKey, session.HardwareId);
            sessionTokenService.Revoke(request.SessionToken);
            return StatusCode(StatusCodes.Status410Gone, new HeartbeatResponse
            {
                Valid = false,
                ErrorCode = "LICENSE_EXPIRED",
                ErrorMessage = "卡密已过期。"
            });
        }

        var success = await licenseService.HeartbeatAsync(session.LicenseKey, session.HardwareId);
        if (!success)
        {
            sessionTokenService.Revoke(request.SessionToken);
            return StatusCode(StatusCodes.Status410Gone, new HeartbeatResponse
            {
                Valid = false,
                ErrorCode = "ACTIVATION_LOST",
                ErrorMessage = "激活记录已被清理，请重新激活。"
            });
        }

        var newExpiry = sessionTokenService.Refresh(request.SessionToken, SessionTokenValidity);

        return Ok(new HeartbeatResponse
        {
            Valid = true,
            NewExpiryUtc = newExpiry,
            LicenseExpiryUtc = license.ExpirationDate
        });
    }

    [HttpPost("logout")]
    [RateLimitingMiddleware(20, "00:01:00")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto request)
    {
        var session = sessionTokenService.Resolve(request.SessionToken);
        if (session != null)
        {
            await licenseService.DisconnectConcurrentLicenseUser(session.LicenseKey, session.HardwareId);
            sessionTokenService.Revoke(request.SessionToken);
        }
        return Ok(new { success = true });
    }

    private static ActivationResponse BuildFailure((string code, string msg) tuple) => new()
    {
        Success = false,
        ErrorCode = tuple.code,
        ErrorMessage = tuple.msg
    };

    private static (string code, string msg) MapValidationException(Exception? ex) => ex switch
    {
        NotFoundException => ("LICENSE_NOT_FOUND", "卡密不存在"),
        ExpiredLicenseException => ("LICENSE_EXPIRED", "卡密已过期"),
        LicenseValidationException lve when lve.Message.Contains("Revoked", StringComparison.OrdinalIgnoreCase)
            => ("LICENSE_REVOKED", "卡密已被吊销"),
        _ => ("LICENSE_INVALID", ex?.Message ?? "卡密无效")
    };

    private static (string code, string msg) MapActivationException(Exception? ex) => ex switch
    {
        MaximumActivationsReachedException => ("LICENSE_FULL", "此卡密的并发激活已达上限，请稍后再试。"),
        NotFoundException => ("LICENSE_NOT_FOUND", "卡密不存在"),
        ExpiredLicenseException => ("LICENSE_EXPIRED", "卡密已过期"),
        _ => ("ACTIVATION_FAILED", ex?.Message ?? "激活失败")
    };
}
