using Aegis.Server.AspNetCore.Entities;
using Aegis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Server.AspNetCore.Data.Context;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : AegisDbContext(options)
{
    public DbSet<User> Users { get; init; }
    public DbSet<Role> Roles { get; init; }
    public DbSet<RefreshToken> RefreshTokens { get; init; }
    public DbSet<LicenseExtension> LicenseExtensions { get; init; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // RefreshToken - User (One-to-One)
        modelBuilder.Entity<RefreshToken>()
            .HasOne(r => r.User)
            .WithOne(u => u.RefreshToken)
            .HasForeignKey<RefreshToken>(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // User - License (One-to-Many, License.UserId 可空 — Wraith 卡密无账号归属)
        modelBuilder.Entity<User>()
            .HasMany(u => u.Licenses)
            .WithOne()
            .HasForeignKey(l => l.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        // LicenseExtension - License (One-to-One, LicenseId as PK)
        modelBuilder.Entity<LicenseExtension>()
            .HasKey(le => le.LicenseId);
        modelBuilder.Entity<LicenseExtension>()
            .HasOne(le => le.License)
            .WithOne()
            .HasForeignKey<LicenseExtension>(le => le.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
