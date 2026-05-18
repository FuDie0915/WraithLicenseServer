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
/// Admin-only endpoints for batch generating Wraith Concurrent license keys.
/// Bypasses Aegis LicenseService.GenerateLicenseAsync (which requires Product/Feature setup)
/// and writes License + LicenseExtension entities directly for a streamlined Wraith flow.
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
    public async Task<IActionResult> List([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 1000);
        var rows = await (from l in dbContext.Licenses.AsNoTracking()
                          where l.Type == LicenseType.Concurrent
                          join e in dbContext.LicenseExtensions.AsNoTracking() on l.LicenseId equals e.LicenseId into eg
                          from e in eg.DefaultIfEmpty()
                          orderby l.IssuedOn descending
                          select new
                          {
                              l.LicenseKey,
                              l.Status,
                              l.IssuedOn,
                              l.ExpirationDate,
                              l.MaxActiveUsersCount,
                              l.ActiveUsersCount,
                              ValidityDays = e == null ? (int?)null : e.ValidityDays,
                              FirstActivatedAt = e == null ? (DateTime?)null : e.FirstActivatedAt
                          })
            .Skip(skip).Take(take).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("revoke")]
    [AuthorizeMiddleware(["Admin"])]
    public async Task<IActionResult> Revoke([FromQuery] string licenseKey)
    {
        var license = await dbContext.Licenses.FirstOrDefaultAsync(l => l.LicenseKey == licenseKey);
        if (license == null) return NotFound();
        license.Status = LicenseStatus.Revoked;
        dbContext.Licenses.Update(license);
        await dbContext.SaveChangesAsync();
        return Ok(new { success = true });
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
