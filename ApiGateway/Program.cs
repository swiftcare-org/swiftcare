var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

// YARP reverse proxy routing, CORS, and gateway forwarding middleware
// are configured in a later stage once AuthService's endpoints exist.

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
