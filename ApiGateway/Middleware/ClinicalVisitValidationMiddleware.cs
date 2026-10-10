using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using ApiGateway.Models;
using ApiGateway.Security;

namespace ApiGateway.Middleware;

public sealed class ClinicalVisitValidationMiddleware(RequestDelegate next, ILogger<ClinicalVisitValidationMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context, IClinicalVisitValidator validator)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? "";
        var consultation = path.Equals("/api/consultations", StringComparison.OrdinalIgnoreCase);
        var prescription = path.Equals("/api/prescriptions", StringComparison.OrdinalIgnoreCase);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var decision = segments.Length == 4 && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("consultations", StringComparison.OrdinalIgnoreCase)
            && segments[3].Equals("no-prescription", StringComparison.OrdinalIgnoreCase);
        if (!HttpMethods.IsPost(context.Request.Method) || !(consultation || prescription || decision))
        {
            await next(context);
            return;
        }
        if (context.User.Identity?.IsAuthenticated != true || !context.User.IsInRole("Doctor"))
        {
            await RejectAsync(context, StatusCodes.Status403Forbidden, "Forbidden");
            return;
        }
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var doctorId) || doctorId == Guid.Empty)
        {
            await RejectAsync(context, StatusCodes.Status401Unauthorized, "Doctor identity is unavailable");
            return;
        }

        Identifiers? identifiers = null;
        context.Request.EnableBuffering(30 * 1024, 1024 * 1024);
        try
        {
            identifiers = await JsonSerializer.DeserializeAsync<Identifiers>(context.Request.Body, JsonOptions, context.RequestAborted);
        }
        catch (JsonException)
        {
            await RejectAsync(context, StatusCodes.Status400BadRequest, "Clinical visit identifiers are invalid");
            return;
        }
        catch (IOException)
        {
            await RejectAsync(context, StatusCodes.Status413PayloadTooLarge, "Clinical request is too large");
            return;
        }
        finally
        {
            context.Request.Body.Position = 0;
        }

        Guid? consultationId = prescription ? identifiers?.ConsultationId : null;
        if (decision)
        {
            if (!Guid.TryParse(segments[2], out var decisionId) || decisionId == Guid.Empty)
            {
                await RejectAsync(context, StatusCodes.Status400BadRequest, "Consultation ID is invalid");
                return;
            }
            consultationId = decisionId;
        }
        if (identifiers is null || identifiers.PatientId == Guid.Empty || identifiers.QueueId == Guid.Empty
            || (!consultation && (!consultationId.HasValue || consultationId == Guid.Empty)))
        {
            await RejectAsync(context, StatusCodes.Status400BadRequest, "Clinical visit identifiers are required");
            return;
        }

        try
        {
            if (!await validator.ValidateAsync(doctorId, identifiers.PatientId, identifiers.QueueId, consultationId, context.RequestAborted))
            {
                await RejectAsync(context, StatusCodes.Status422UnprocessableEntity, "Clinical visit does not match an eligible visit owned by this doctor");
                return;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
            || exception is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Clinical visit validation is unavailable.");
            await RejectAsync(context, StatusCodes.Status503ServiceUnavailable, "Clinical visit validation is temporarily unavailable");
            return;
        }
        await next(context);
    }

    private static Task RejectAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new MessageResponse(message), context.RequestAborted);
    }

    private sealed record Identifiers(Guid PatientId, Guid QueueId, Guid? ConsultationId);
}
