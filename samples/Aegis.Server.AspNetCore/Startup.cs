using System.Text.Json;
using Aegis.Server.AspNetCore.Data.Context;
using Aegis.Server.AspNetCore.DTOs;
using Aegis.Server.AspNetCore.Entities;
using Aegis.Server.AspNetCore.Filters;
using Aegis.Server.AspNetCore.Services;
using Aegis.Server.Data;
using Aegis.Server.Extensions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Aegis.Server.AspNetCore;

public class Startup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        // 1. Configure Logging
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        // 2. Configure Database (SQLite)
        services.AddControllers();
        services.AddDbContext<AegisDbContext, ApplicationDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        // 3. Configure Filters
        services.AddMvc(options => { options.Filters.Add<ApiExceptionFilter>(); });

        // 4. Configure Settings
        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

        // 5. Register Services
        services.AddScoped<AuthService>();
        services.AddAegisServer();
        services.AddMemoryCache();
        services.AddSingleton<ClientSessionTokenService>();
        services.AddSerilog();

        // 6. Configure Swagger
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        // Initialize database (EnsureCreated + seed roles & admin user)
        using (var scope = app.ApplicationServices.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
            DatabaseSeeder.SeedAsync(db, scope.ServiceProvider, configuration).GetAwaiter().GetResult();
        }

        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseHttpsRedirection();
        }

        app.UseSerilogRequestLogging()
            .UseRouting()
            .UseAuthentication()
            .UseAuthorization()
            .UseEndpoints(endpoints => { endpoints.MapControllers(); })
            .UseExceptionHandler(appError =>
            {
                appError.Run(async context =>
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    context.Response.ContentType = "application/json";

                    var contextFeature = context.Features.Get<IExceptionHandlerFeature>();
                    if (contextFeature != null)
                    {
                        var error = new { message = contextFeature.Error.Message };
                        await context.Response.WriteAsync(JsonSerializer.Serialize(error));

                        // Logging
                        Log.Error(contextFeature.Error, "An unhandled exception occurred.");
                    }
                });
            });
    }
}
