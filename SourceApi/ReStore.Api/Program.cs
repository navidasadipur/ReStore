using ReStore.Api.Extensions;
using ReStore.Application.IoC;
using ReStore.Infrastructure.IoC;
using ReStore.Infrastructure.Payments;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();
app.Logger.LogInformation("ZarinPal configuration mode: {Mode}",
    app.Services.GetRequiredService<ZarinpalSettings>().Mode);
await app.InitializeDatabaseAsync();
app.UseApi();
await app.RunAsync();
