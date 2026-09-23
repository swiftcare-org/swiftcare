using Microsoft.EntityFrameworkCore;
using PrescriptionService.Data;
using PrescriptionService.Middleware;
using PrescriptionService.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddDbContext<PrescriptionDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("PrescriptionDb"),
        new MySqlServerVersion(new Version(8, 4, 0))));
builder.Services.AddScoped<IPrescriptionService, PrescriptionCreationService>();
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

public partial class Program;
