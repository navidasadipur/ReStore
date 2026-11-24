using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ReStore.Api.DTOs;
using ReStore.Api.Filters;
using ReStore.Api.Mcp;
using ReStore.Api.Services;
using Refit;
using ReStore.Application.Interfaces;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Api.Extensions;

public static class ApiServiceRegistration
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRefitClient<IMcpChatClient>().ConfigureHttpClient(client =>
        {
            client.BaseAddress = new Uri(configuration["McpClient:BaseUrl"]!);
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("McpClient:TimeoutSeconds", 120));
        });
        services.AddControllers().ConfigureApiBehaviorOptions(options =>
        {
            var defaultResponseFactory = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context =>
            {
                if (context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnvelopeResponseAttribute>() is null)
                    return defaultResponseFactory(context);

                var errors = context.ModelState.Where(entry => entry.Value?.Errors.Count > 0)
                    .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors
                        .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage)
                        .ToArray());
                return new OkObjectResult(new ApiErrorResponse("The request contains invalid values.", "ValidationFailed", errors));
            };
        });

        services.AddMcpServer().WithHttpTransport().WithTools<ProductMcpTools>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Jwt auth header", Name = "Authorization", In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey, Scheme = "Bearer"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                        Scheme = "oauth2", Name = "Bearer", In = ParameterLocation.Header
                    },
                    new List<string>()
                }
            });
        });

        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["app"])
            .AddDbContextCheck<StoreContext>("postgresql", tags: ["db"]);
        services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials().WithOrigins("http://localhost:3000")));

        var tokenKey = configuration["JWTSettings:TokenKey"] ?? throw new InvalidOperationException(
            "JWTSettings:TokenKey is not configured (set the JWTSettings__TokenKey environment variable).");
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey))
            };
        });
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<IBuyerIdentity, HttpBuyerIdentity>();
        return services;
    }
}
