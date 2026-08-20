using System.Text;
using AuthService.Data;
using AuthService.Models.Configuration;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var authDbConnectionString = builder.Configuration.GetConnectionString("AuthDb")
    ?? throw new InvalidOperationException(
        "Connection string 'ConnectionStrings:AuthDb' is not configured. Set it via the " +
        "ConnectionStrings__AuthDb environment variable.");

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseMySql(authDbConnectionString, new MySqlServerVersion(new Version(8, 4, 0))));

var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException(
        "Jwt:SecretKey is not configured. Set it via the Jwt__SecretKey environment variable.");

if (Encoding.UTF8.GetByteCount(jwtSecretKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey must be at least 32 bytes long to be used with HS256.");
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Gateway-secret enforcement middleware and the login controller are wired up in a later stage.

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var seederLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DevelopmentSeeder.SeedAsync(dbContext, app.Configuration, seederLogger);
}

app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
