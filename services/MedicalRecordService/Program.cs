using MedicalRecordService.Data;
using MedicalRecordService.Middleware;
using MedicalRecordService.Services;
using Scalar.AspNetCore;

if (args.Contains("--migrate"))
{
    Environment.ExitCode = await SchemaInstaller.RunAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<IMedicalRecordConnectionFactory, MySqlMedicalRecordConnectionFactory>();
builder.Services.AddScoped<IConsultationRepository, AdoNetConsultationRepository>();
builder.Services.AddScoped<IConsultationTemplateService, ConsultationTemplateService>();
builder.Services.AddScoped<IConsultationService, ConsultationService>();
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

public partial class Program;
