using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ReStore.Api.Middleware;
using ReStore.Domain.Entities;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Api.Extensions;

public static class ApiApplicationExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<StoreContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<StoreContext>>();
        try
        {
            await context.Database.MigrateAsync();
            await DbInitializer.Initialize(context, users);
        }
        catch (Exception exception)
        {
            // Retain startup's existing degraded mode when the database is unavailable.
            logger.LogError(exception, "Problem migrating data");
        }
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseRouting();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapMcp("/mcp").AllowAnonymous();
        app.MapHealthChecks("/health/app", HealthOptions("app")).AllowAnonymous();
        app.MapHealthChecks("/health/db", HealthOptions("db")).AllowAnonymous();
        app.MapFallbackToController("Index", "Fallback");
        return app;
    }

    private static HealthCheckOptions HealthOptions(string tag) => new()
    {
        Predicate = check => check.Tags.Contains(tag),
        ResponseWriter = WriteHealthCheckResponse
    };

    private static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key, status = entry.Value.Status.ToString(),
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2), description = entry.Value.Description
            })
        });
    }
}
