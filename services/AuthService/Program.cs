using System.Text;
using System.Text.Json.Serialization;
using AuthService.Data;
using AuthService.Maintenance;
using AuthService.Middleware;
using AuthService.Models.Configuration;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;

// Maintenance commands run to completion and exit; they never start the web host.
var maintenanceCommand = MaintenanceCommandParser.Parse(args);
if (maintenanceCommand != MaintenanceCommand.None)
{
    return await MaintenanceCommandRunner.RunAsync(maintenanceCommand);
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// CreateUserRequest.Role is bound from a JSON string ("Doctor"/"Receptionist"/"Admin"),
// matching how AuthController already serializes Role via .ToString() in responses.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// The connection string is read lazily by EF Core when AuthDbContext is first resolved,
// so a missing value here doesn't crash registration - the explicit check below (after
// Build()) is what actually fails startup fast.
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseMySql(builder.Configuration.GetConnectionString("AuthDb"), new MySqlServerVersion(new Version(8, 4, 0))));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserAccountService, UserAccountService>();

var app = builder.Build();

// Fail fast before serving any request if required configuration is missing. Checked
// against app.Configuration (post-Build) rather than builder.Configuration so that test
// hosts which inject configuration during Build() (e.g. WebApplicationFactory) are honored.
var authDbConnectionString = app.Configuration.GetConnectionString("AuthDb");
if (string.IsNullOrEmpty(authDbConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:AuthDb' is not configured. Set it via the " +
        "ConnectionStrings__AuthDb environment variable.");
}

var jwtSecretKey = app.Configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(jwtSecretKey) || Encoding.UTF8.GetByteCount(jwtSecretKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey is not configured or is under 32 bytes. Set it via the Jwt__SecretKey environment variable.");
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

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var seederLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DevelopmentSeeder.SeedAsync(dbContext, app.Configuration, seederLogger);
}

app.UseMiddleware<GatewaySecretMiddleware>();

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

return 0;

public partial class Program;
