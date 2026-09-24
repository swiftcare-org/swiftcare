using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PrescriptionService.Data;
using PrescriptionService.Maintenance;
using PrescriptionService.Middleware;
using PrescriptionService.Services;
using Scalar.AspNetCore;

// Azure Container Apps Jobs run maintenance commands to completion without
// starting Kestrel or exposing an application endpoint.
var maintenanceCommand = MaintenanceCommandParser.Parse(args);
if (maintenanceCommand != MaintenanceCommand.None)
{
    return await MaintenanceCommandRunner.RunAsync(maintenanceCommand);
}

var builder = WebApplication.CreateBuilder(args);

// Telemetry is opt-in: local runs, CI and tests set no connection string and skip it entirely.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .ConfigureResource(resource => resource.AddService("swiftcare-prescription"))
        .WithTracing(tracing => tracing.AddSource("MySqlConnector"));
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<PrescriptionDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("PrescriptionDb"),
        new MySqlServerVersion(new Version(8, 4, 0))));
builder.Services.AddScoped<IPrescriptionService, PrescriptionManagementService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("PrescriptionDb")))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:PrescriptionDb' is not configured. Set it via the " +
        "ConnectionStrings__PrescriptionDb environment variable.");
}

if (string.IsNullOrWhiteSpace(app.Configuration["Gateway:InternalSecret"]))
{
    throw new InvalidOperationException(
        "Gateway:InternalSecret is not configured. Set it via the " +
        "Gateway__InternalSecret environment variable.");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseMiddleware<GatewaySecretMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

return 0;

public partial class Program;
