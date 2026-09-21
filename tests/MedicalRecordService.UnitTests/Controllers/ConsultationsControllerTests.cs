using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public class ConsultationsControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string UserNameHeaderName = "X-User-Name";
    private const string RoomNumberHeaderName = "X-Room-Number";

    [Fact]
    public async Task CreateAsDoctorReturnsConsultationLinkedToQueueAndDoctor()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var request = ValidRequest();
        var doctorId = Guid.NewGuid();
        var expected = new ConsultationResponse
        {
            Id = Guid.NewGuid(),
            QueueId = request.QueueId,
            PatientId = request.PatientId,
            DoctorId = doctorId,
            DoctorName = "Dr. Amara Chen",
            RoomNumber = "R-204",
            Symptoms = request.Symptoms,
            ExaminationFindings = request.ExaminationFindings,
            Diagnosis = request.Diagnosis,
            Notes = request.Notes,
            ConsultationDate = new DateTime(2026, 9, 13, 8, 45, 0, DateTimeKind.Utc)
        };
        factory.ConsultationServiceMock
            .Setup(service => service.CreateAsync(
                It.Is<CreateConsultationRequest>(candidate =>
                    candidate.QueueId == request.QueueId
                    && candidate.PatientId == request.PatientId),
                doctorId,
                "Dr. Amara Chen",
                "R-204",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateConsultationResult
            {
                Outcome = CreateConsultationOutcome.Success,
                Consultation = expected
            });
        var client = CreateDoctorClient(factory, doctorId);

        var response = await client.PostAsJsonAsync("/api/consultations", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsultationResponse>();
        Assert.Equal(expected.Id, body!.Id);
        Assert.Equal(request.QueueId, body.QueueId);
        Assert.Equal(request.PatientId, body.PatientId);
        Assert.Equal(doctorId, body.DoctorId);
        Assert.Equal("Dr. Amara Chen", body.DoctorName);
        Assert.Equal("R-204", body.RoomNumber);
        Assert.Equal(expected.ConsultationDate, body.ConsultationDate);
    }

    [Theory]
    [InlineData("", "Confirmed diagnosis", "Symptoms")]
    [InlineData("   ", "Confirmed diagnosis", "Symptoms")]
    [InlineData("Reported symptoms", "", "Diagnosis")]
    [InlineData("Reported symptoms", "   ", "Diagnosis")]
    public async Task CreateWithMissingRequiredFieldReturnsValidationError(
        string symptoms,
        string diagnosis,
        string expectedField)
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var client = CreateDoctorClient(factory, Guid.NewGuid());
        var request = ValidRequest(symptoms, diagnosis);

        var response = await client.PostAsJsonAsync("/api/consultations", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.True(errors.ContainsKey(expectedField));
        factory.ConsultationServiceMock.Verify(
            service => service.CreateAsync(
                It.IsAny<CreateConsultationRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateWithPastFollowUpDateReturns400WithFieldError()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        factory.ConsultationServiceMock
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateConsultationRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateConsultationResult
            {
                Outcome = CreateConsultationOutcome.FollowUpDateInPast
            });
        var client = CreateDoctorClient(factory, Guid.NewGuid());

        var response = await client.PostAsJsonAsync("/api/consultations", ValidRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal(
            "Follow-up date cannot be in the past",
            Assert.Single(errors[nameof(CreateConsultationRequest.FollowUpDate)]));
    }

    [Fact]
    public async Task CreateWithoutCompleteDoctorIdentityReturns401()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Doctor");

        var response = await client.PostAsJsonAsync("/api/consultations", ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.ConsultationServiceMock.Verify(
            service => service.CreateAsync(
                It.IsAny<CreateConsultationRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task CreateAsNonDoctorReturns403(string role)
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, role);

        var response = await client.PostAsJsonAsync("/api/consultations", ValidRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConsultationEndpointWithoutGatewaySecretReturns401()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Doctor");

        var response = await client.PostAsJsonAsync("/api/consultations", ValidRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient CreateClientWithRole(
        MedicalRecordServiceWebApplicationFactory factory,
        string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            GatewaySecretHeaderName,
            MedicalRecordServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        return client;
    }

    private static HttpClient CreateDoctorClient(
        MedicalRecordServiceWebApplicationFactory factory,
        Guid doctorId)
    {
        var client = CreateClientWithRole(factory, "Doctor");
        client.DefaultRequestHeaders.Add(UserIdHeaderName, doctorId.ToString());
        client.DefaultRequestHeaders.Add(UserNameHeaderName, "Dr. Amara Chen");
        client.DefaultRequestHeaders.Add(RoomNumberHeaderName, "R-204");
        return client;
    }

    private static CreateConsultationRequest ValidRequest(
        string symptoms = "Reported symptoms",
        string diagnosis = "Confirmed diagnosis") =>
        new()
        {
            QueueId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            Symptoms = symptoms,
            ExaminationFindings = "Recorded findings",
            Diagnosis = diagnosis,
            Notes = "Consultation notes"
        };

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorsAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errorsElement = document.RootElement.GetProperty("errors");

        return errorsElement
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value
                    .EnumerateArray()
                    .Select(element => element.GetString()!)
                    .ToArray());
    }
}
