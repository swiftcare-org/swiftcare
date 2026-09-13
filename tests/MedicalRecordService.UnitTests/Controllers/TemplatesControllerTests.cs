using System.Net;
using System.Net.Http.Json;
using MedicalRecordService.Models.Dtos;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public class TemplatesControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";

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
            Notes = "Assessment and plan:\n- "
        };
        factory.TemplateServiceMock
            .Setup(service => service.GetActiveTemplatesAsync(It.IsAny<CancellationToken>()))
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
        factory.TemplateServiceMock.Verify(
            service => service.GetActiveTemplatesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
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
}
