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
