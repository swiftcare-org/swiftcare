using QueueService.Data;
using QueueService.Maintenance;
using QueueService.Middleware;
using QueueService.Models.Configuration;
using QueueService.Services;
using Scalar.AspNetCore;
using Microsoft.EntityFrameworkCore;

// Azure Container Apps Jobs run maintenance commands to completion without
// starting Kestrel or exposing an application endpoint.
var maintenanceCommand = MaintenanceCommandParser.Parse(args);
if (maintenanceCommand != MaintenanceCommand.None)
{
    return await MaintenanceCommandRunner.RunAsync(maintenanceCommand);
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// The connection string is read lazily by EF Core when QueueDbContext is first resolved,
// so a missing value here doesn't crash startup - the explicit check below (after
// Build()) is what actually fails startup fast.
builder.Services.AddDbContext<QueueDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("QueueDb"), new MySqlServerVersion(new Version(8, 4, 0))));

builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection("Queue"));
builder.Services.AddScoped<IQueueEntryCreationService, QueueEntryCreationService>();

var app = builder.Build();

// Fail fast before serving any request if required configuration is missing. Checked
// against app.Configuration (post-Build) rather than builder.Configuration so that test
// hosts which inject configuration during Build() (e.g. WebApplicationFactory) are honored -
// matching the pattern used by PatientService's own startup checks.
var queueDbConnectionString = app.Configuration.GetConnectionString("QueueDb");
if (string.IsNullOrEmpty(queueDbConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:QueueDb' is not configured. Set it via the " +
        "ConnectionStrings__QueueDb environment variable.");
}

if (string.IsNullOrEmpty(app.Configuration["Gateway:InternalSecret"]))
{
    throw new InvalidOperationException(
        "Gateway:InternalSecret is not configured. Set it via the Gateway__InternalSecret environment variable.");
}

// Checked for presence only, never reachability: QueueService must start and serve
// /health even when Kafka is down, matching independent deployability - the
// patient-checked-in consumer (added in a later stage) degrades instead of blocking startup.
if (string.IsNullOrEmpty(app.Configuration["Kafka:BootstrapServers"]))
{
    throw new InvalidOperationException(
        "Kafka:BootstrapServers is not configured. Set it via the Kafka__BootstrapServers environment variable.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive API docs at /scalar/v1, reading the /openapi/v1.json document MapOpenApi
    // above serves. Development-only, matching MapOpenApi's own guard - never expose an
    // API explorer against a production service.
    app.MapScalarApiReference();
}

app.UseMiddleware<GatewaySecretMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

return 0;

public partial class Program;
