using ReStore.Api.Extensions;
using ReStore.Application.IoC;
using ReStore.Infrastructure.IoC;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();
await app.InitializeDatabaseAsync();
app.UseApi();
await app.RunAsync();
