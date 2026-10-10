using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ApiGateway.Middleware;
using ApiGateway.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiGateway.UnitTests.Middleware;

public sealed class ClinicalVisitValidationTests
{
    private static readonly Guid Doctor = Guid.NewGuid(), Patient = Guid.NewGuid(), Queue = Guid.NewGuid(), Consultation = Guid.NewGuid();
    private static readonly string Body = JsonSerializer.Serialize(new { PatientId = Patient, QueueId = Queue, ConsultationId = Consultation, symptoms = "Unchanged body" });

    private static DefaultHttpContext Context(string path, string body = "", string method = "POST", string? role = "Doctor", string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId ?? Doctor.ToString()), new Claim("role", role ?? "")], "Test", "sub", "role"));
        return context;
    }

    [Theory]
    [InlineData("/api/consultations", false)]
    [InlineData("/API/CONSULTATIONS/", false)]
    [InlineData("/api/prescriptions", true)]
    [InlineData("/API/PRESCRIPTIONS/", true)]
    [InlineData("decision", true)]
    public async Task ValidVisitForwardsTheOriginalBodyAndTrustedDoctor(string path, bool outcome)
    {
        if (path == "decision") path = $"/api/consultations/{Consultation}/no-prescription/";
        var context = Context(path, Body);
        context.Request.Headers["X-User-Id"] = Guid.NewGuid().ToString();
        var validator = new Validator();
        var forwarded = false;
        await new ClinicalVisitValidationMiddleware(async ctx =>
        {
            forwarded = true;
            Assert.Equal(Body, await new StreamReader(ctx.Request.Body).ReadToEndAsync());
        }, NullLogger<ClinicalVisitValidationMiddleware>.Instance).InvokeAsync(context, validator);
        Assert.True(forwarded);
        Assert.Equal((Doctor, Patient, Queue, outcome ? (Guid?)Consultation : null), Assert.Single(validator.Calls));
    }

    [Fact]
    public async Task DecisionUsesTheRouteConsultationInsteadOfABodyOverride()
    {
        var routeId = Guid.NewGuid();
        var context = Context($"/api/consultations/{routeId}/no-prescription", Body);
        var validator = new Validator();
        await new ClinicalVisitValidationMiddleware(_ => Task.CompletedTask, NullLogger<ClinicalVisitValidationMiddleware>.Instance)
            .InvokeAsync(context, validator);
        Assert.Equal(routeId, Assert.Single(validator.Calls).Item4);
    }

    [Theory]
    [InlineData("GET", "/api/consultations")]
    [InlineData("PUT", "/api/prescriptions")]
    [InlineData("POST", "/api/consultations/other")]
    [InlineData("POST", "/other/consultations/id/no-prescription")]
    [InlineData("POST", "/api/other/id/no-prescription")]
    [InlineData("POST", "/api/consultations/id/other")]
    [InlineData("POST", "/api/consultations/id/no-prescription/extra")]
    public async Task OtherRequestsSkipClinicalValidation(string method, string path)
    {
        var validator = new Validator();
        var forwarded = false;
        await new ClinicalVisitValidationMiddleware(_ => { forwarded = true; return Task.CompletedTask; },
            NullLogger<ClinicalVisitValidationMiddleware>.Instance).InvokeAsync(Context(path, method: method), validator);
        Assert.True(forwarded);
        Assert.Empty(validator.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("invalid")]
    [InlineData("{\"patientId\":null}")]
    [InlineData("{\"patientId\":\"invalid\"}")]
    public async Task InvalidBodyNeverReachesTheSaveEndpoint(string body)
    {
        await RejectedAsync(Context("/api/consultations", body), 400, "Clinical visit identifiers");
    }

    [Theory]
    [InlineData("patient")]
    [InlineData("queue")]
    [InlineData("consultation")]
    [InlineData("missingConsultation")]
    public async Task MissingIdentifiersNeverReachTheSaveEndpoint(string missing)
    {
        var body = JsonSerializer.Serialize(new
        {
            PatientId = missing == "patient" ? Guid.Empty : Patient,
            QueueId = missing == "queue" ? Guid.Empty : Queue,
            ConsultationId = missing == "missingConsultation" ? (Guid?)null : missing == "consultation" ? Guid.Empty : Consultation
        });
        await RejectedAsync(Context("/api/prescriptions", body), 400, "Clinical visit identifiers are required");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidDecisionRouteIsRejected(string id)
    {
        await RejectedAsync(Context($"/api/consultations/{id}/no-prescription", Body), 400, "Consultation");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Receptionist")]
    [InlineData("")]
    public async Task NonDoctorCannotWriteClinicalData(string role) =>
        await RejectedAsync(Context("/api/consultations", Body, role: role), 403, "Forbidden");

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task MissingDoctorIdentityIsRejected(string userId) =>
        await RejectedAsync(Context("/api/consultations", Body, userId: userId), 401, "Doctor identity");

    [Fact]
    public async Task MismatchedOrIneligibleVisitReturns422WithoutForwarding()
    {
        await RejectedAsync(Context("/api/consultations", Body), 422, "eligible visit", new Validator { Result = false });
    }

    [Fact]
    public async Task AnUnauthenticatedDoctorClaimCannotAuthorizeAWrite()
    {
        var context = Context("/api/consultations", Body);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Doctor")]));
        await RejectedAsync(context, 403, "Forbidden");
    }

    [Fact]
    public async Task AnUnreadableRequestCannotReachTheSaveEndpoint()
    {
        var context = Context("/api/consultations", Body);
        context.Request.Body = new FailedReadStream();
        await RejectedAsync(context, 413, "too large");
    }

    [Theory]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("configuration")]
    [InlineData("timeout")]
    public async Task UnavailableAuthorityFailsClosed(string failure)
    {
        Exception exception = failure switch
        {
            "http" => new HttpRequestException(),
            "json" => new JsonException(),
            "configuration" => new InvalidOperationException(),
            _ => new OperationCanceledException()
        };
        await RejectedAsync(Context("/api/prescriptions", Body), 503, "temporarily unavailable", new Validator { Failure = exception });
    }

    [Fact]
    public async Task ClientCancellationPropagates()
    {
        var context = Context("/api/prescriptions", Body);
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;
        var validator = new Validator { Before = () => cancellation.Cancel(), Failure = new OperationCanceledException() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ClinicalVisitValidationMiddleware(_ => throw new InvalidOperationException("Must not save"),
                NullLogger<ClinicalVisitValidationMiddleware>.Instance).InvokeAsync(context, validator));
    }

    private static async Task RejectedAsync(DefaultHttpContext context, int status, string message, Validator? validator = null)
    {
        var forwarded = false;
        await new ClinicalVisitValidationMiddleware(_ => { forwarded = true; return Task.CompletedTask; },
            NullLogger<ClinicalVisitValidationMiddleware>.Instance).InvokeAsync(context, validator ?? new Validator());
        Assert.False(forwarded);
        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Contains(message, await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    private sealed class Validator : IClinicalVisitValidator
    {
        public bool Result { get; init; } = true;
        public Exception? Failure { get; init; }
        public Action? Before { get; init; }
        public List<(Guid, Guid, Guid, Guid?)> Calls { get; } = [];
        public Task<bool> ValidateAsync(Guid doctorId, Guid patientId, Guid queueId, Guid? consultationId, CancellationToken cancellationToken)
        {
            Calls.Add((doctorId, patientId, queueId, consultationId));
            Before?.Invoke();
            return Failure is null ? Task.FromResult(Result) : Task.FromException<bool>(Failure);
        }
    }

    private sealed class FailedReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Request body exceeds the buffering limit");
    }
}
