using Confluent.Kafka;
using MedicalRecordService.Data;
using MedicalRecordService.Maintenance;
using MedicalRecordService.Middleware;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MedicalRecordService.Services;
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

// Telemetry is opt-in: local runs, CI and tests set no connection string and skip it entirely.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .ConfigureResource(resource => resource.AddService("swiftcare-medical-record"))
        .WithTracing(tracing => tracing.AddSource("MySqlConnector"));
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IMedicalRecordConnectionFactory, MySqlMedicalRecordConnectionFactory>();
builder.Services.AddScoped<IConsultationRepository, AdoNetConsultationRepository>();
builder.Services.AddScoped<IConsultationCompletionRepository, AdoNetConsultationCompletionRepository>();
builder.Services.AddScoped<IVitalSignsRepository, AdoNetVitalSignsRepository>();
builder.Services.AddScoped<IConsultationTemplateService, ConsultationTemplateService>();
builder.Services.AddScoped<IConsultationService, ConsultationService>();
builder.Services.AddScoped<IConsultationCompletionService, ConsultationCompletionService>();
builder.Services.AddScoped<IVitalSignsService, VitalSignsService>();
builder.Services.Configure<KafkaCompletionOptions>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IProducer<string, string>>(services =>
{
    var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<KafkaCompletionOptions>>().Value;
    return new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = options.BootstrapServers,
        MessageTimeoutMs = options.MessageTimeoutMs
    }).Build();
});
builder.Services.AddSingleton<IConsultationCompletedPublisher, KafkaConsultationCompletedPublisher>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("MedicalRecordDb")))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:MedicalRecordDb' is not configured. Set it via the " +
        "ConnectionStrings__MedicalRecordDb environment variable.");
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
