using ApiGateway.Middleware;

const string FrontendCorsPolicy = "FrontendCorsPolicy";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddCors(options =>
{
    // The React frontend (port 5173) is the only client permitted to call the Gateway directly.
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors(FrontendCorsPolicy);

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GatewayForwardingMiddleware>();

app.MapHealthChecks("/health");
app.MapReverseProxy();

app.Run();
