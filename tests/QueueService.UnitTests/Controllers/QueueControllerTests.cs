using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using QueueService.Models.Dtos;
using QueueService.Models.Enums;
using QueueService.Services;

namespace QueueService.UnitTests.Controllers;

public class QueueControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string UserNameHeaderName = "X-User-Name";
    private const string RoomNumberHeaderName = "X-Room-Number";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    private static HttpClient CreateClientWithRole(
        QueueServiceWebApplicationFactory factory,
        string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, QueueServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        return client;
    }

    private static HttpClient CreateDoctorClient(
        QueueServiceWebApplicationFactory factory,
        Guid doctorId,
        string correlationId = "corr-call-next")
    {
        var client = CreateClientWithRole(factory, "Doctor");
        client.DefaultRequestHeaders.Add(UserIdHeaderName, doctorId.ToString());
        client.DefaultRequestHeaders.Add(UserNameHeaderName, "Dr. Amara Chen");
        client.DefaultRequestHeaders.Add(RoomNumberHeaderName, "R-204");
        client.DefaultRequestHeaders.Add(CorrelationIdHeaderName, correlationId);
        return client;
    }

    [Fact]
    public async Task GetDisplayWithoutUserIdentityReturnsOnlyPublicQueueData()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var expected = new WaitingRoomDisplayResponse
        {
            CurrentRooms =
            [
                new RoomQueueAssignmentResponse
                {
                    RoomNumber = "2",
                    QueueNumber = "Q-007"
                }
            ],
            NextQueueNumbers = ["Q-008", "Q-009", "Q-010"]
        };
        factory.TodayQueueServiceMock
            .Setup(service => service.GetDisplayAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            GatewaySecretHeaderName,
            QueueServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.GetAsync("/api/queue/display");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("patient", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("doctor", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("checkedIn", json, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            ["currentRooms", "nextQueueNumbers"],
            document.RootElement
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name));
        Assert.Equal(
            ["queueNumber", "roomNumber"],
            document.RootElement
                .GetProperty("currentRooms")[0]
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name));

        var body = await response.Content.ReadFromJsonAsync<WaitingRoomDisplayResponse>();
        var room = Assert.Single(body!.CurrentRooms);
        Assert.Equal("2", room.RoomNumber);
        Assert.Equal("Q-007", room.QueueNumber);
        Assert.Equal(["Q-008", "Q-009", "Q-010"], body.NextQueueNumbers);
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
    public async Task CallNextAsDoctorReturnsAssignedPatient()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var doctorId = Guid.NewGuid();
        var expected = new CalledPatientResponse
        {
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            QueueNumber = "Q-007",
            Status = "IN_CONSULTATION",
            DoctorId = doctorId,
            DoctorName = "Dr. Amara Chen",
            RoomNumber = "R-204",
            CalledAt = new DateTime(2026, 9, 8, 6, 30, 0, DateTimeKind.Utc)
        };
        factory.CallNextPatientServiceMock
            .Setup(service => service.CallNextAsync(
                doctorId,
                "Dr. Amara Chen",
                "R-204",
                "corr-call-next",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallNextPatientResult
            {
                Outcome = CallNextPatientOutcome.Success,
                CalledPatient = expected
            });
        var client = CreateDoctorClient(factory, doctorId);

        var response = await client.PutAsync("/api/queue/call-next", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CalledPatientResponse>();
        Assert.Equal(expected.QueueId, body!.QueueId);
        Assert.Equal(expected.PatientId, body.PatientId);
        Assert.Equal("Q-007", body.QueueNumber);
        Assert.Equal("IN_CONSULTATION", body.Status);
        Assert.Equal(doctorId, body.DoctorId);
        Assert.Equal("Dr. Amara Chen", body.DoctorName);
        Assert.Equal("R-204", body.RoomNumber);
        Assert.Equal(expected.CalledAt, body.CalledAt);
    }

    [Fact]
    public async Task CallNextWhenPoolIsEmptyReturns404WithRequiredMessage()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var doctorId = Guid.NewGuid();
        factory.CallNextPatientServiceMock
            .Setup(service => service.CallNextAsync(
                doctorId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallNextPatientResult
            {
                Outcome = CallNextPatientOutcome.NoPatientsWaiting
            });
        var client = CreateDoctorClient(factory, doctorId);

        var response = await client.PutAsync("/api/queue/call-next", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("No patients currently waiting", body!.Message);
    }

    [Fact]
    public async Task CallNextWhenDoctorOrRoomIsOccupiedReturns409WithRequiredMessage()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var doctorId = Guid.NewGuid();
        factory.CallNextPatientServiceMock
            .Setup(service => service.CallNextAsync(
                doctorId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CallNextPatientResult
            {
                Outcome = CallNextPatientOutcome.DoctorOrRoomOccupied
            });
        var client = CreateDoctorClient(factory, doctorId);

        var response = await client.PutAsync("/api/queue/call-next", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Complete current consultation first", body!.Message);
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    [InlineData("Nurse")]
    public async Task CallNextAsNonDoctorReturns403(string role)
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, role);

        var response = await client.PutAsync("/api/queue/call-next", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.CallNextPatientServiceMock.Verify(
            service => service.CallNextAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CallNextWithIncompleteDoctorIdentityReturns401()
    {
        using var factory = new QueueServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Doctor");

        var response = await client.PutAsync("/api/queue/call-next", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.CallNextPatientServiceMock.Verify(
            service => service.CallNextAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
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
