using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ReStore.Api.DTOs;
using ReStore.Api.Filters;

namespace ReStore.Api.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;
        public ExceptionMiddleware(RequestDelegate next,
                                   ILogger<ExceptionMiddleware> logger,
                                   IHostEnvironment env)
        {
            _env = env;
            _logger = logger;
            _next = next;

        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);

                // If the response has already started there is nothing we can
                // change — writing here would throw and mask the real error.
                if (context.Response.HasStarted) throw;

                // Only feature endpoints opt in. BuggyController's deliberate errors,
                // webhooks, MCP and non-API responses retain their HTTP semantics.
                if (context.GetEndpoint()?.Metadata.GetMetadata<EnvelopeResponseAttribute>() is not null)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                        _env.IsDevelopment() ? ex.Message : "Internal server error", "UnhandledError"));
                    return;
                }

                context.Response.ContentType = "application/json";
                context.Response.StatusCode = 500;

                // Exception messages can carry connection strings, SQL and file
                // paths, so only expose them outside of production.
                var response = new ProblemDetails
                {
                    Status = 500,
                    Detail = _env.IsDevelopment() ? ex.StackTrace?.ToString() : null,
                    Title = _env.IsDevelopment() ? ex.Message : "Internal server error"
                };

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(response, options);

                await context.Response.WriteAsync(json);
            }
        }
    }
}
