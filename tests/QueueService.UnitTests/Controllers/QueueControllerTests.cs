using System.Net;
using System.Net.Http.Json;
using Moq;
using QueueService.Models.Dtos;
using QueueService.Services;

namespace QueueService.UnitTests.Controllers;

public class QueueControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";

    private static HttpClient CreateClientWithRole(
        QueueServiceWebApplicationFactory factory,
        string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, QueueServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        return client;
    }

    [Fact]
    public async Task GetTodayAsReceptionistReturnsQueueEntries()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var expected = new TodayQueueEntryResponse
        {
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            QueueNumber = "Q-003",
            CheckedInAt = new DateTime(2026, 9, 2, 5, 30, 0, DateTimeKind.Utc),
            Status = "WAITING"
        };
        factory.TodayQueueServiceMock
            .Setup(service => service.GetTodayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.GetAsync("/api/queue/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TodayQueueEntryResponse>>();
        var entry = Assert.Single(body!);
        Assert.Equal(expected.QueueId, entry.QueueId);
        Assert.Equal(expected.PatientId, entry.PatientId);
        Assert.Equal("Q-003", entry.QueueNumber);
        Assert.Equal("WAITING", entry.Status);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    [InlineData("Nurse")]
    public async Task GetTodayAsNonReceptionistReturns403(string role)
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, role);

        var response = await client.GetAsync("/api/queue/today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.TodayQueueServiceMock.Verify(
            service => service.GetTodayAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTodayWithoutGatewaySecretReturns401()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.GetAsync("/api/queue/today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.TodayQueueServiceMock.Verify(
            service => service.GetTodayAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetWaitingAsDoctorReturnsWaitingPool()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var expected = new TodayQueueEntryResponse
        {
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            QueueNumber = "Q-004",
            CheckedInAt = new DateTime(2026, 9, 2, 5, 30, 0, DateTimeKind.Utc),
            Status = "WAITING"
        };
        factory.TodayQueueServiceMock
            .Setup(service => service.GetWaitingAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);
        var client = CreateClientWithRole(factory, "Doctor");

        var response = await client.GetAsync("/api/queue/today/waiting");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TodayQueueEntryResponse>>();
        var entry = Assert.Single(body!);
        Assert.Equal(expected.QueueId, entry.QueueId);
        Assert.Equal(expected.PatientId, entry.PatientId);
        Assert.Equal("Q-004", entry.QueueNumber);
        Assert.Equal("WAITING", entry.Status);
        Assert.Null(entry.RoomNumber);
        Assert.Null(entry.DoctorName);
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    [InlineData("Nurse")]
    public async Task GetWaitingAsNonDoctorReturns403(string role)
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, role);

        var response = await client.GetAsync("/api/queue/today/waiting");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.TodayQueueServiceMock.Verify(
            service => service.GetWaitingAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetWaitingWithoutGatewaySecretReturns401()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Doctor");

        var response = await client.GetAsync("/api/queue/today/waiting");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.TodayQueueServiceMock.Verify(
            service => service.GetWaitingAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTodayPatientStatusAsReceptionistReturnsQueueStatus()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.PatientQueueStatusServiceMock
            .Setup(service => service.GetTodayStatusAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PatientQueueStatusResponse { IsCheckedIn = true, QueueNumber = "Q-003" });
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.GetAsync($"/api/queue/today/patient/{patientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PatientQueueStatusResponse>();
        Assert.True(body!.IsCheckedIn);
        Assert.Equal("Q-003", body.QueueNumber);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    [InlineData("Nurse")]
    public async Task GetTodayPatientStatusAsNonReceptionistReturns403(string role)
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, role);

        var response = await client.GetAsync($"/api/queue/today/patient/{patientId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.PatientQueueStatusServiceMock.Verify(
            service => service.GetTodayStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTodayPatientStatusWithoutGatewaySecretReturns401()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.GetAsync($"/api/queue/today/patient/{patientId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.PatientQueueStatusServiceMock.Verify(
            service => service.GetTodayStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
