using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PrescriptionService.Controllers;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Controllers;

// SWC-130: sends real HTTP requests through routing, model binding and validation, so the
// identity headers and the request body are proven to bind the way the Gateway sends them.
public class NoPrescriptionEndpointTests
{
    private static readonly Guid ConsultationId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly Guid QueueId = Guid.NewGuid();
    private static readonly Guid PatientId = Guid.NewGuid();

    private static string Path => $"/api/consultations/{ConsultationId}/no-prescription";

    [Fact]
    public async Task DoctorRequestIsBoundFromRouteHeadersAndBodyAndReturns201()
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        service.Setup(candidate => candidate.RecordAsync(
                ConsultationId,
                It.Is<RecordNoPrescriptionRequest>(request =>
                    request.QueueId == QueueId && request.PatientId == PatientId),
                DoctorId,
                "Dr. Amara Chen",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecordNoPrescriptionResult(
                RecordNoPrescriptionOutcome.Recorded,
                new PrescriptionResponse(
                    Guid.NewGuid(),
                    ConsultationId,
                    QueueId,
                    PatientId,
                    DoctorId,
                    "Dr. Amara Chen",
                    "NOT_REQUIRED",
                    new DateTime(2026, 10, 7, 4, 30, 0, DateTimeKind.Utc),
                    [])));
        await using var app = await StartAsync(service);

        using var response = await SendAsync(
            app, new { queueId = QueueId, patientId = PatientId }, "Doctor", DoctorId.ToString(), "Dr. Amara Chen");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NOT_REQUIRED", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Dr. Amara Chen", body.RootElement.GetProperty("doctorName").GetString());
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    [InlineData(null)]
    public async Task RequestWithoutTheDoctorRoleHeaderReturns403(string? role)
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        await using var app = await StartAsync(service);

        using var response = await SendAsync(
            app, new { queueId = QueueId, patientId = PatientId }, role, DoctorId.ToString(), "Staff Member");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "Dr. Amara Chen")]
    [InlineData("4d8f6f0e-3b0f-4b5a-9d6e-7f1c2a9b8c11", null)]
    public async Task DoctorRequestWithoutAnIdentityHeaderReturns401(string? userId, string? doctorName)
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        await using var app = await StartAsync(service);

        using var response = await SendAsync(
            app, new { queueId = QueueId, patientId = PatientId }, "Doctor", userId, doctorName);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    // An ID left out of the body is reported as missing, not silently read as all zeros.
    [Fact]
    public async Task BodyWithoutTheIdsReturns400NamingEachMissingField()
    {
        var service = new Mock<INoPrescriptionService>(MockBehavior.Strict);
        await using var app = await StartAsync(service);

        using var response = await SendAsync(app, new { }, "Doctor", DoctorId.ToString(), "Dr. Amara Chen");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.Equal("Queue ID is required", errors.GetProperty("QueueId")[0].GetString());
        Assert.Equal("Patient ID is required", errors.GetProperty("PatientId")[0].GetString());
        service.VerifyNoOtherCalls();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplication app,
        object body,
        string? role,
        string? userId,
        string? doctorName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(body) };
        AddHeader(request, "X-User-Role", role);
        AddHeader(request, "X-User-Id", userId);
        AddHeader(request, "X-User-Name", doctorName);

        return await app.GetTestClient().SendAsync(request);
    }

    private static void AddHeader(HttpRequestMessage request, string name, string? value)
    {
        if (value is not null)
        {
            request.Headers.Add(name, value);
        }
    }

    // A minimal host with the real controllers, in memory. Only the service is replaced.
    private static async Task<WebApplication> StartAsync(Mock<INoPrescriptionService> service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(NoPrescriptionController).Assembly);
        builder.Services.AddSingleton(service.Object);

        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
