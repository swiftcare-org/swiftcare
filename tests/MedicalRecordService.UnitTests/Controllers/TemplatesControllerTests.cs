using System.Net;
using System.Net.Http.Json;
using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Models.Enums;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public class TemplatesControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private static readonly Guid DoctorId = Guid.NewGuid();

    [Fact]
    public async Task GetTemplatesAsDoctorReturnsEditablePrefillFields()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var expected = new ConsultationTemplateResponse
        {
            Id = Guid.NewGuid(),
            Name = "General Consultation",
            Symptoms = "Presenting symptoms:\n- ",
            ExaminationFindings = "Examination findings:\n- ",
            Notes = "Assessment and plan:\n- ",
            IsBuiltIn = true
        };
        factory.TemplateServiceMock
            .Setup(service => service.GetTemplatesForDoctorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);
        var client = CreateClientWithRole(factory, "Doctor");

        var response = await client.GetAsync("/api/templates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var templates = await response.Content.ReadFromJsonAsync<List<ConsultationTemplateResponse>>();
        var template = Assert.Single(templates!);
        Assert.Equal(expected.Id, template.Id);
        Assert.Equal("General Consultation", template.Name);
        Assert.Equal("Presenting symptoms:\n- ", template.Symptoms);
        Assert.Equal("Examination findings:\n- ", template.ExaminationFindings);
        Assert.Equal("Assessment and plan:\n- ", template.Notes);
        Assert.True(template.IsBuiltIn);
    }

    // The list is scoped by the Gateway identity, so one doctor can never ask for another's.
    [Fact]
    public async Task GetTemplatesAsksOnlyForTheSignedInDoctorsTemplates()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        factory.TemplateServiceMock
            .Setup(service => service.GetTemplatesForDoctorAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateClientWithRole(factory, "Doctor").GetAsync($"/api/templates?doctorId={Guid.NewGuid()}");

        factory.TemplateServiceMock.Verify(
            service => service.GetTemplatesForDoctorAsync(DoctorId, It.IsAny<CancellationToken>()),
            Times.Once);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task GetTemplatesAsNonDoctorReturns403(string role)
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, role);

        var response = await client.GetAsync("/api/templates");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetTemplatesWithoutAUsableDoctorIdReturns401(string? userId)
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Doctor", userId);

        var response = await client.GetAsync("/api/templates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Doctor identity is unavailable", body!.Message);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    private static HttpClient CreateClientWithRole(
        MedicalRecordServiceWebApplicationFactory factory,
        string role) => CreateClientWithRole(factory, role, DoctorId.ToString());

    private static HttpClient CreateClientWithRole(
        MedicalRecordServiceWebApplicationFactory factory,
        string role,
        string? userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            GatewaySecretHeaderName,
            MedicalRecordServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add(UserIdHeaderName, userId);
        }

        return client;
    }

    private static object ValidTemplateBody() => new
    {
        Name = "BP Review",
        Symptoms = "Headache:\n- ",
        ExaminationFindings = "Blood pressure:\n- ",
        Notes = "Plan:\n- "
    };

    [Fact]
    public async Task CreateTemplateAsDoctorReturns201WithTheSavedTemplate()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        var savedId = Guid.NewGuid();
        factory.TemplateServiceMock
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateConsultationTemplateRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTemplateResult
            {
                Outcome = CreateTemplateOutcome.Created,
                Template = new ConsultationTemplateResponse
                {
                    Id = savedId,
                    Name = "BP Review",
                    Symptoms = "Headache:\n- ",
                    ExaminationFindings = "Blood pressure:\n- ",
                    Notes = "Plan:\n- ",
                    IsBuiltIn = false
                }
            });

        var response = await CreateClientWithRole(factory, "Doctor").PostAsJsonAsync("/api/templates", ValidTemplateBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var template = await response.Content.ReadFromJsonAsync<ConsultationTemplateResponse>();
        Assert.Equal(savedId, template!.Id);
        Assert.Equal("BP Review", template.Name);
        Assert.False(template.IsBuiltIn);
    }

    // The owner comes from the Gateway identity. An owner sent in the body is ignored.
    [Fact]
    public async Task CreateTemplateTakesTheOwnerFromTheGatewayIdentityNotTheBody()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        factory.TemplateServiceMock
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateConsultationTemplateRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTemplateResult { Outcome = CreateTemplateOutcome.DuplicateName });

        await CreateClientWithRole(factory, "Doctor").PostAsJsonAsync("/api/templates", new
        {
            Name = "BP Review",
            Symptoms = "Headache",
            ExaminationFindings = "Blood pressure",
            Notes = "Plan",
            CreatedByDoctorId = Guid.NewGuid(),
            DoctorId = Guid.NewGuid()
        });

        factory.TemplateServiceMock.Verify(
            service => service.CreateAsync(
                It.Is<CreateConsultationTemplateRequest>(request => request.Name == "BP Review"),
                DoctorId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Null(typeof(CreateConsultationTemplateRequest).GetProperty("CreatedByDoctorId"));
        Assert.Null(typeof(CreateConsultationTemplateRequest).GetProperty("DoctorId"));
    }

    [Fact]
    public async Task CreateTemplateWithADuplicateNameReturns409WithExactMessage()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        factory.TemplateServiceMock
            .Setup(service => service.CreateAsync(
                It.IsAny<CreateConsultationTemplateRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTemplateResult { Outcome = CreateTemplateOutcome.DuplicateName });

        var response = await CreateClientWithRole(factory, "Doctor").PostAsJsonAsync("/api/templates", ValidTemplateBody());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("A template with this name already exists", body!.Message);
    }

    [Fact]
    public async Task CreateTemplateWithEmptyFieldsReturns400AndNeverCallsTheService()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();

        var response = await CreateClientWithRole(factory, "Doctor").PostAsJsonAsync(
            "/api/templates", new { Name = " ", Symptoms = "", ExaminationFindings = "", Notes = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(["Template name is required"], problem!.Errors["Name"]);
        Assert.Equal(["Symptoms are required"], problem.Errors["Symptoms"]);
        Assert.Equal(["Examination findings are required"], problem.Errors["ExaminationFindings"]);
        Assert.Equal(["Notes are required"], problem.Errors["Notes"]);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task CreateTemplateAsNonDoctorReturns403AndNeverCallsTheService(string role)
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();

        var response = await CreateClientWithRole(factory, role).PostAsJsonAsync("/api/templates", ValidTemplateBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateTemplateWithoutADoctorIdReturns401AndNeverCallsTheService()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();

        var response = await CreateClientWithRole(factory, "Doctor", userId: null)
            .PostAsJsonAsync("/api/templates", ValidTemplateBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    // A "Created" outcome with no template is a programming error, not a client error.
    [Fact]
    public async Task CreatedOutcomeWithoutATemplateIsNotReportedAsSuccess()
    {
        var service = new Mock<IConsultationTemplateService>();
        service
            .Setup(candidate => candidate.CreateAsync(
                It.IsAny<CreateConsultationTemplateRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateTemplateResult { Outcome = CreateTemplateOutcome.Created });
        var context = new DefaultHttpContext();
        context.Request.Headers[UserRoleHeaderName] = "Doctor";
        context.Request.Headers[UserIdHeaderName] = DoctorId.ToString();
        var controller = new TemplatesController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.CreateTemplate(new CreateConsultationTemplateRequest(), CancellationToken.None));

        Assert.Equal("A successful template result must include the created template.", exception.Message);
    }

    private static readonly Guid TemplateId = Guid.NewGuid();

    private static void SetupRemove(MedicalRecordServiceWebApplicationFactory factory, RemoveTemplateOutcome outcome) =>
        factory.TemplateServiceMock
            .Setup(service => service.RemoveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

    [Fact]
    public async Task RemoveOwnTemplateReturns204AndPassesTheTemplateAndTheSignedInDoctor()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        SetupRemove(factory, RemoveTemplateOutcome.Removed);

        var response = await CreateClientWithRole(factory, "Doctor").DeleteAsync($"/api/templates/{TemplateId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        factory.TemplateServiceMock.Verify(
            service => service.RemoveAsync(TemplateId, DoctorId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveBuiltInTemplateReturns403WithExactMessage()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        SetupRemove(factory, RemoveTemplateOutcome.BuiltIn);

        var response = await CreateClientWithRole(factory, "Doctor").DeleteAsync($"/api/templates/{TemplateId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Built-in templates cannot be removed", body!.Message);
    }

    [Fact]
    public async Task RemoveMissingTemplateReturns404WithExactMessage()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();
        SetupRemove(factory, RemoveTemplateOutcome.NotFound);

        var response = await CreateClientWithRole(factory, "Doctor").DeleteAsync($"/api/templates/{TemplateId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Template not found", body!.Message);
    }

    [Fact]
    public async Task RemoveWithTheAllZeroIdReturns400AndNeverCallsTheService()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();

        var response = await CreateClientWithRole(factory, "Doctor")
            .DeleteAsync("/api/templates/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Template ID must be provided.", body!.Message);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task RemoveTemplateAsNonDoctorReturns403AndNeverCallsTheService(string role)
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();

        var response = await CreateClientWithRole(factory, role).DeleteAsync($"/api/templates/{TemplateId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveTemplateWithoutADoctorIdReturns401AndNeverCallsTheService()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();

        var response = await CreateClientWithRole(factory, "Doctor", userId: null)
            .DeleteAsync($"/api/templates/{TemplateId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.TemplateServiceMock.VerifyNoOtherCalls();
    }
}
