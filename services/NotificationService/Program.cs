using Azure.Monitor.OpenTelemetry.AspNetCore;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotificationService.Data;
using NotificationService.Maintenance;
using NotificationService.Middleware;
using NotificationService.Models.Configuration;
using NotificationService.Services;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

// Azure Container Apps Jobs run maintenance commands to completion without
// starting Kestrel or exposing an application endpoint.
var maintenanceCommand = MaintenanceCommandParser.Parse(args);
if (maintenanceCommand != MaintenanceCommand.None)
{
    return await MaintenanceCommandRunner.RunAsync(maintenanceCommand);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// The connection string is read lazily by EF Core when NotificationDbContext is first
// resolved, so a missing value here does not crash registration. The explicit check
// below (after Build()) is what fails startup fast.
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("NotificationDb"),
        new MySqlServerVersion(new Version(8, 4, 0))));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<INotificationRecorder, NotificationRecorder>();
builder.Services.AddScoped<INotificationFeedService, NotificationFeedService>();
builder.Services.AddScoped<IDailyReportService, DailyReportService>();
builder.Services.AddScoped<IMonthlyReportService, MonthlyReportService>();

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.Configure<ReportOptions>(builder.Configuration.GetSection("Reports"));

// Registered as a singleton: IConsumer is not thread-safe for concurrent Consume() calls,
// but only ActivityEventConsumer's single loop ever calls it. Keeping it injectable lets
// tests replace the Kafka dependency.
builder.Services.AddSingleton<IConsumer<string, string>>(services =>
{
    var kafkaOptions = services.GetRequiredService<IOptions<KafkaOptions>>().Value;
    return new ConsumerBuilder<string, string>(new ConsumerConfig
    {
        BootstrapServers = kafkaOptions.BootstrapServers,
        GroupId = kafkaOptions.ConsumerGroupId,
        EnableAutoCommit = false,
        // A new consumer group starts from the oldest event still on the topic, so the
        // feed is not empty the first time the service runs.
        AutoOffsetReset = AutoOffsetReset.Earliest
    }).Build();
});
builder.Services.AddHostedService<ActivityEventConsumer>();

// Telemetry is opt-in: local runs, CI and tests set no connection string and skip it entirely.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .ConfigureResource(resource => resource.AddService("swiftcare-notification"))
        .WithTracing(tracing => tracing.AddSource("MySqlConnector"));
}

var app = builder.Build();

// Fail fast before serving any request if required configuration is missing. Checked
// against app.Configuration (post-Build) so that test hosts which inject configuration
// during Build() (for example WebApplicationFactory) are honored.
RequiredSettings.EnsurePresent(app.Configuration);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Interactive API docs at /scalar/v1. Development only, matching MapOpenApi's own guard.
    app.MapScalarApiReference();
}

app.UseMiddleware<GatewaySecretMiddleware>();

app.MapHealthChecks("/health");
app.MapControllers();

await app.RunAsync();

return 0;

public partial class Program
{
    protected Program()
    {
    }
}
