using ApiGateway.Models;
using ApiGateway.Security;
using System.Text.Json;

namespace ApiGateway.Middleware;

public sealed class SessionValidationMiddleware(RequestDelegate next, ILogger<SessionValidationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ISessionValidator sessions)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }
        var revoke = HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.Value?.TrimEnd('/').Equals("/api/auth/logout", StringComparison.OrdinalIgnoreCase) == true;
        try
        {
            if (!await sessions.ValidateAsync(context.User, revoke, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new MessageResponse("Unauthorized"), context.RequestAborted);
                return;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
            || exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Session validation is unavailable.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new MessageResponse("Session validation is temporarily unavailable"), context.RequestAborted);
            return;
        }
        await next(context);
    }
}
