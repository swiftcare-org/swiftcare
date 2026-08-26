using PatientService.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Fail fast before serving any request if required configuration is missing. Checked
// against app.Configuration (post-Build) rather than builder.Configuration so that test
// hosts which inject configuration during Build() (e.g. WebApplicationFactory) are honored -
// matching the pattern used by AuthService's own startup checks.
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
