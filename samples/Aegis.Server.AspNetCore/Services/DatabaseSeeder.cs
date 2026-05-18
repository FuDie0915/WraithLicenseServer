using Aegis.Server.AspNetCore.Data.Context;
using Aegis.Server.AspNetCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Server.AspNetCore.Services;

/// <summary>
/// First-run database initializer: ensures Admin/User roles exist
/// and an "admin" account is seeded from AdminSeed configuration.
/// </summary>
internal static class DatabaseSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, IServiceProvider services, IConfiguration configuration)
    {
        // 1. Seed roles
        var roleNames = new[] { "Admin", "User" };
        foreach (var name in roleNames)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name))
                db.Roles.Add(new Role { Name = name });
        }
        await db.SaveChangesAsync();

        // 2. Seed initial admin user from configuration
        var seed = configuration.GetSection("AdminSeed");
        var username = seed["Username"] ?? "admin";
        var email = seed["Email"] ?? "admin@wraith.local";
        var password = seed["Password"];

        if (string.IsNullOrWhiteSpace(password)) return;
        if (await db.Users.AnyAsync(u => u.Username == username)) return;

        var auth = services.GetRequiredService<AuthService>();
        var user = new User
        {
            Username = username,
            Email = email,
            FullName = "Administrator",
            Role = "Admin",
            PasswordHash = auth.HashPassword(password)
        };
        await db.Users.AddAsync(user);
        await db.SaveChangesAsync();
    }
}
