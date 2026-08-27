using System.Text.Json.Serialization;
using Confluent.Kafka;
using PatientService.Data;
using PatientService.Maintenance;
using Scalar.AspNetCore;
using PatientService.Middleware;
using PatientService.Models.Configuration;
using PatientService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Azure Container Apps Jobs run maintenance commands to completion without
// starting Kestrel or exposing an application endpoint.
var maintenanceCommand = MaintenanceCommandParser.Parse(args);
if (maintenanceCommand != MaintenanceCommand.None)
{
    return await MaintenanceCommandRunner.RunAsync(maintenanceCommand);
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Gender/BloodGroup are bound and serialized as JSON strings ("Male", "A+"), matching how
// AuthService's CreateUserRequest.Role round-trips through JsonStringEnumConverter.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// The connection string is read lazily by EF Core when PatientDbContext is first resolved,
// so a missing value here doesn't crash registration - the explicit check below (after
// Build()) is what actually fails startup fast.
builder.Services.AddDbContext<PatientDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("PatientDb"), new MySqlServerVersion(new Version(8, 4, 0))));

builder.Services.Configure<KafkaOptions>(builder.Configuration.GetSection("Kafka"));

// Registered as a singleton - IProducer is thread-safe and expensive to construct, so one
// is built per process rather than per request. Registered as its own service (rather than
// built inline inside KafkaPatientEventPublisher) so tests can substitute a fake producer.
builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var kafkaOptions = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
    return new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = kafkaOptions.BootstrapServers,
        MessageTimeoutMs = kafkaOptions.MessageTimeoutMs
    }).Build();
});
builder.Services.AddSingleton<IPatientEventPublisher, KafkaPatientEventPublisher>();
builder.Services.AddScoped<IPatientRegistrationService, PatientRegistrationService>();
builder.Services.AddScoped<IPatientSearchService, PatientSearchService>();

var app = builder.Build();

// Fail fast before serving any request if required configuration is missing. Checked
// against app.Configuration (post-Build) rather than builder.Configuration so that test
// hosts which inject configuration during Build() (e.g. WebApplicationFactory) are honored -
// matching the pattern used by AuthService's own startup checks.
var patientDbConnectionString = app.Configuration.GetConnectionString("PatientDb");
if (string.IsNullOrEmpty(patientDbConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:PatientDb' is not configured. Set it via the " +
        "ConnectionStrings__PatientDb environment variable.");
}

if (string.IsNullOrEmpty(app.Configuration["Gateway:InternalSecret"]))
{
    throw new InvalidOperationException(
        "Gateway:InternalSecret is not configured. Set it via the Gateway__InternalSecret environment variable.");
}

// Checked for presence only, never reachability: PatientService must start and serve
// /health even when Kafka is down, matching independent deployability - a lost broker
// degrades registration (see PatientRegistrationService) rather than blocking startup.
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

// Bounded so a slow or unreachable broker cannot hang application shutdown - the default
// IProducer.Dispose() flush has no such bound.
app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Services.GetRequiredService<IProducer<string, string>>().Flush(TimeSpan.FromSeconds(5));
});

app.UseMiddleware<GatewaySecretMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

return 0;

public partial class Program;
