using System.Net;
using System.Net.Http.Json;
using MedicalRecordService.Models.Dtos;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public class CompletedConsultationsAuthorizationTests
{
    [Theory]
    [InlineData("Doctor", MedicalRecordServiceWebApplicationFactory.ValidGatewaySecret, HttpStatusCode.OK)]
    [InlineData("Admin", MedicalRecordServiceWebApplicationFactory.ValidGatewaySecret, HttpStatusCode.Forbidden)]
    [InlineData(null, MedicalRecordServiceWebApplicationFactory.ValidGatewaySecret, HttpStatusCode.Forbidden)]
    [InlineData("Doctor", null, HttpStatusCode.Unauthorized)]
    [InlineData("Doctor", "invalid-secret", HttpStatusCode.Unauthorized)]
    public async Task CompletedVisitsRequireValidatedGatewayAndDoctorRole(string? role, string? secret, HttpStatusCode status)
    {
        await using var factory = new MedicalRecordServiceWebApplicationFactory();
        var doctor = Guid.NewGuid();
        var visit = new CompletedConsultationContextResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        factory.CompletionServiceMock.Setup(service => service.FindCompletedPageAsync(doctor, 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/consultations/completed?page=0");
        request.Headers.Add("X-User-Id", doctor.ToString());
        if (role is not null) request.Headers.Add("X-User-Role", role);
        if (secret is not null) request.Headers.Add("X-Gateway-Secret", secret);

        using var response = await client.SendAsync(request);

        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.OK)
        {
            Assert.Equal(visit, Assert.Single((await response.Content.ReadFromJsonAsync<CompletedConsultationContextResponse[]>())!));
            factory.CompletionServiceMock.VerifyAll();
        }
        else
        {
            factory.CompletionServiceMock.VerifyNoOtherCalls();
        }
    }
}
