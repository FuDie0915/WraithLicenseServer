using Aegis.Server.AspNetCore.Data.Context;
using Aegis.Server.AspNetCore.Services;
using Microsoft.EntityFrameworkCore;

namespace Aegis.Server.AspNetCore;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        // Initialize database BEFORE host.RunAsync() so that any IHostedService
        // (e.g. HeartbeatMonitor) sees a fully-created schema on first query.
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            await DatabaseSeeder.SeedAsync(db, scope.ServiceProvider, config);
        }

        await host.RunAsync();
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
    }
}
