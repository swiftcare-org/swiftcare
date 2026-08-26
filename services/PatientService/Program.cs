using System.Text.Json.Serialization;
using PatientService.Data;
using PatientService.Middleware;
using Microsoft.EntityFrameworkCore;

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<GatewaySecretMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
