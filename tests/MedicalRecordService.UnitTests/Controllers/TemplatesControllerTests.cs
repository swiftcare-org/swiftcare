using System.Net;
using System.Net.Http.Json;
using MedicalRecordService.Models.Dtos;
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
}
