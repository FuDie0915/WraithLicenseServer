using System.Security.Cryptography;
using System.Text;
using Aegis.Enums;
using Aegis.Server.AspNetCore.Attributes;
using Aegis.Server.AspNetCore.Data.Context;
using Aegis.Server.AspNetCore.DTOs.Activation;
using Aegis.Server.AspNetCore.Entities;
using Aegis.Server.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Server.AspNetCore.Controllers;

/// <summary>
/// Admin-only endpoints for managing Wraith Concurrent license keys.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class AdminLicensesController(ApplicationDbContext dbContext) : ControllerBase
{
    private const string WraithProductName = "Wraith";

    // Charset: 30 chars, no easily-confused glyphs (I/L/O/0/1 removed)
    private const string KeyCharset = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int KeySegments = 6;
    private const int KeySegmentLength = 4;

    [HttpPost("generate")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> Generate([FromBody] GenerateLicensesRequest request)
    {
        if (request.Count is < 1 or > 10000)
            return BadRequest(new { error = "Count must be between 1 and 10000." });
        if (request.ValidityDays is < 1 or > 3650)
            return BadRequest(new { error = "ValidityDays must be between 1 and 3650." });
        if (request.MaxConcurrentUsers is < 1 or > 1000)
            return BadRequest(new { error = "MaxConcurrentUsers must be between 1 and 1000." });

        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.ProductName == WraithProductName);
        if (product == null)
        {
            product = new Product { ProductId = Guid.NewGuid(), ProductName = WraithProductName };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync();
        }

        var now = DateTime.UtcNow;
        var licenses = new List<License>(request.Count);
        var extensions = new List<LicenseExtension>(request.Count);

        for (var i = 0; i < request.Count; i++)
        {
            var licenseId = Guid.NewGuid();
            var license = new License
            {
                LicenseId = licenseId,
                LicenseKey = GenerateFormattedKey(),
                Type = LicenseType.Concurrent,
                Status = LicenseStatus.Valid,
                IssuedOn = now,
                ExpirationDate = null,
                Issuer = request.Issuer,
                IssuedTo = request.IssuedTo,
                MaxActiveUsersCount = request.MaxConcurrentUsers,
                ActiveUsersCount = 0,
                ProductId = product.ProductId
            };
            licenses.Add(license);
            extensions.Add(new LicenseExtension
            {
                LicenseId = licenseId,
                ValidityDays = request.ValidityDays,
                FirstActivatedAt = null
            });
        }

        await dbContext.Licenses.AddRangeAsync(licenses);
        await dbContext.LicenseExtensions.AddRangeAsync(extensions);
        await dbContext.SaveChangesAsync();

        var csv = new StringBuilder();
        csv.AppendLine("LicenseKey,ValidityDays,MaxConcurrentUsers,IssuedOn");
        foreach (var l in licenses)
            csv.AppendLine($"{l.LicenseKey},{request.ValidityDays},{request.MaxConcurrentUsers},{l.IssuedOn:O}");

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv",
            $"wraith-licenses-{now:yyyyMMddHHmmss}.csv");
    }

    [HttpGet("list")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null)
    {
        take = Math.Clamp(take, 1, 1000);

        var q = from l in dbContext.Licenses.AsNoTracking()
                where l.Type == LicenseType.Concurrent
                join e in dbContext.LicenseExtensions.AsNoTracking() on l.LicenseId equals e.LicenseId into eg
                from e in eg.DefaultIfEmpty()
                select new { l, e };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            // 卡密精确匹配 OR 备注包含
            q = q.Where(x => x.l.LicenseKey == s || EF.Functions.Like(x.l.IssuedTo, "%" + s + "%"));
        }

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<LicenseStatus>(status, true, out var st))
        {
            q = q.Where(x => x.l.Status == st);
        }

        var total = await q.CountAsync();

        var items = await q
            .OrderByDescending(x => x.l.IssuedOn)
            .Skip(skip).Take(take)
            .Select(x => new
            {
                x.l.LicenseKey,
                x.l.Status,
                x.l.IssuedOn,
                x.l.ExpirationDate,
                x.l.MaxActiveUsersCount,
                x.l.ActiveUsersCount,
                x.l.IssuedTo,
                ValidityDays = x.e == null ? (int?)null : x.e.ValidityDays,
                FirstActivatedAt = x.e == null ? (DateTime?)null : x.e.FirstActivatedAt
            })
            .ToListAsync();

        return Ok(new { total, items });
    }

    [HttpPost("update")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> Update([FromBody] UpdateLicenseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LicenseKey))
            return BadRequest(new { error = "LicenseKey is required." });

        var license = await dbContext.Licenses.FirstOrDefaultAsync(l => l.LicenseKey == request.LicenseKey);
        if (license == null) return NotFound();

        var ext = await dbContext.LicenseExtensions.FirstOrDefaultAsync(e => e.LicenseId == license.LicenseId);

        if (request.ValidityDays.HasValue)
        {
            if (request.ValidityDays is < 1 or > 3650)
                return BadRequest(new { error = "ValidityDays must be between 1 and 3650." });
            if (ext != null)
            {
                ext.ValidityDays = request.ValidityDays.Value;
                // 若已激活，同步重算过期时间
                if (ext.FirstActivatedAt.HasValue)
                    license.ExpirationDate = ext.FirstActivatedAt.Value.AddDays(request.ValidityDays.Value);
            }
        }

        if (request.MaxConcurrentUsers.HasValue)
        {
            if (request.MaxConcurrentUsers is < 1 or > 1000)
                return BadRequest(new { error = "MaxConcurrentUsers must be between 1 and 1000." });
            license.MaxActiveUsersCount = request.MaxConcurrentUsers.Value;
        }

        if (request.IssuedTo != null)
        {
            license.IssuedTo = request.IssuedTo;
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            Enum.TryParse<LicenseStatus>(request.Status, true, out var st))
        {
            license.Status = st;
        }

        await dbContext.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("revoke")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> Revoke([FromQuery] string licenseKey)
    {
        var license = await dbContext.Licenses.FirstOrDefaultAsync(l => l.LicenseKey == licenseKey);
        if (license == null) return NotFound();
        license.Status = LicenseStatus.Revoked;
        await dbContext.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPost("revoke-batch")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> RevokeBatch([FromBody] BatchLicenseKeysRequest request)
    {
        if (request.LicenseKeys == null || request.LicenseKeys.Count == 0)
            return BadRequest(new { error = "LicenseKeys is required." });

        var affected = await dbContext.Licenses
            .Where(l => request.LicenseKeys.Contains(l.LicenseKey))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, LicenseStatus.Revoked));

        return Ok(new { success = true, affected });
    }

    [HttpPost("unrevoke-batch")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> UnrevokeBatch([FromBody] BatchLicenseKeysRequest request)
    {
        if (request.LicenseKeys == null || request.LicenseKeys.Count == 0)
            return BadRequest(new { error = "LicenseKeys is required." });

        var affected = await dbContext.Licenses
            .Where(l => request.LicenseKeys.Contains(l.LicenseKey) && l.Status == LicenseStatus.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, LicenseStatus.Valid));

        return Ok(new { success = true, affected });
    }

    [HttpPost("delete")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> Delete([FromBody] BatchLicenseKeysRequest request)
    {
        if (request.LicenseKeys == null || request.LicenseKeys.Count == 0)
            return BadRequest(new { error = "LicenseKeys is required." });

        var ids = await dbContext.Licenses
            .Where(l => request.LicenseKeys.Contains(l.LicenseKey))
            .Select(l => l.LicenseId)
            .ToListAsync();

        if (ids.Count == 0) return Ok(new { success = true, affected = 0 });

        await dbContext.Activations.Where(a => ids.Contains(a.LicenseId)).ExecuteDeleteAsync();
        await dbContext.LicenseExtensions.Where(e => ids.Contains(e.LicenseId)).ExecuteDeleteAsync();
        var affected = await dbContext.Licenses.Where(l => ids.Contains(l.LicenseId)).ExecuteDeleteAsync();

        return Ok(new { success = true, affected });
    }

    private static string GenerateFormattedKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeySegments * KeySegmentLength);
        var sb = new StringBuilder(KeySegments * (KeySegmentLength + 1) - 1);
        for (var i = 0; i < bytes.Length; i++)
        {
            sb.Append(KeyCharset[bytes[i] % KeyCharset.Length]);
            if (i % KeySegmentLength == KeySegmentLength - 1 && i < bytes.Length - 1)
                sb.Append('-');
        }
        return sb.ToString();
    }
}

public sealed class UpdateLicenseRequest
{
    public string LicenseKey { get; set; } = string.Empty;
    public int? ValidityDays { get; set; }
    public int? MaxConcurrentUsers { get; set; }
    public string? IssuedTo { get; set; }
    public string? Status { get; set; }
}

public sealed class BatchLicenseKeysRequest
{
    public List<string> LicenseKeys { get; set; } = new();
}
