using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ApiGateway.Middleware;
using ApiGateway.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiGateway.UnitTests.Middleware;

// Each rejection writes exactly one response with its own message, and only the failures the
// middleware knows about are reported as an unavailable authority (SWC-151 mutation testing).
public sealed class ClinicalVisitValidationEdgeCaseTests
{
    private static readonly Guid Doctor = Guid.NewGuid(), Patient = Guid.NewGuid(), Queue = Guid.NewGuid();

    [Fact]
    public async Task UnreadableBodyIsRejectedWithOnlyItsOwnMessage()
    {
        var context = Context("/api/consultations", "invalid");

        await InvokeAsync(context, new Validator());

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Clinical visit identifiers are invalid", await ReadMessageAsync(context));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidDecisionRouteIsRejectedWithOnlyItsOwnMessage(string consultationId)
    {
        var body = JsonSerializer.Serialize(new { PatientId = Patient, QueueId = Queue });
        var context = Context($"/api/consultations/{consultationId}/no-prescription", body);

        await InvokeAsync(context, new Validator());

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal("Consultation ID is invalid", await ReadMessageAsync(context));
    }

    // A programming error must surface as one, not be reported as an unavailable authority.
    [Fact]
    public async Task UnexpectedValidatorFailureIsNotReportedAsUnavailable()
    {
        var body = JsonSerializer.Serialize(new { PatientId = Patient, QueueId = Queue });
        var context = Context("/api/consultations", body);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            InvokeAsync(context, new Validator { Failure = new NotSupportedException() }));
        Assert.NotEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
    }

    private static Task InvokeAsync(DefaultHttpContext context, IClinicalVisitValidator validator) =>
        new ClinicalVisitValidationMiddleware(
                _ => throw new InvalidOperationException("Must not reach the save endpoint"),
                NullLogger<ClinicalVisitValidationMiddleware>.Instance)
            .InvokeAsync(context, validator);

    // Parsing fails if a second response was written after the first.
    private static async Task<string?> ReadMessageAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return JsonDocument.Parse(body).RootElement.GetProperty("message").GetString();
    }

    private static DefaultHttpContext Context(string path, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, Doctor.ToString()), new Claim("role", "Doctor")],
            "Test", "sub", "role"));
        return context;
    }

    private sealed class Validator : IClinicalVisitValidator
    {
        public Exception? Failure { get; init; }

        public Task<bool> ValidateAsync(Guid doctorId, Guid patientId, Guid queueId, Guid? consultationId,
            CancellationToken cancellationToken) =>
            Failure is null ? Task.FromResult(true) : Task.FromException<bool>(Failure);
    }
}
