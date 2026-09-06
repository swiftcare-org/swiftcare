using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using PatientService.Models.Dtos;
using PatientService.Services;

namespace PatientService.UnitTests.Controllers;

public class ChronicConditionsControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private static HttpClient CreateClientWithRole(
        PatientServiceWebApplicationFactory factory,
        string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            GatewaySecretHeaderName,
            PatientServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        client.DefaultRequestHeaders.Add(UserIdHeaderName, Guid.NewGuid().ToString());
        return client;
    }

    private static object ValidConditionBody() => new
    {
        ConditionName = "Type 2 Diabetes",
        DateDiagnosed = "2020-03-12",
        Notes = "Controlled with medication"
    };

    private static ChronicConditionResponse SampleCondition() => new()
    {
        ConditionId = Guid.NewGuid(),
        ConditionName = "Type 2 Diabetes",
        DateDiagnosed = new DateOnly(2020, 3, 12),
        Notes = "Controlled with medication"
    };

    [Fact]
    public async Task AddConditionAsReceptionistReturns201()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var expected = SampleCondition();
        factory.ChronicConditionServiceMock
            .Setup(service => service.AddConditionAsync(
                patientId,
                It.IsAny<ChronicConditionRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync(
            $"/api/patients/{patientId}/conditions",
            ValidConditionBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ChronicConditionResponse>();
        Assert.Equal(expected.ConditionId, body!.ConditionId);
        Assert.Equal(expected.DateDiagnosed, body.DateDiagnosed);
    }

    [Fact]
    public async Task AddConditionWithMissingNameReturns400WithoutCallingService()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync(
            $"/api/patients/{patientId}/conditions",
            new { ConditionName = "", DateDiagnosed = "2020-03-12" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("Condition name is required", errors["ConditionName"]);
        factory.ChronicConditionServiceMock.Verify(
            service => service.AddConditionAsync(
                It.IsAny<Guid>(),
                It.IsAny<ChronicConditionRequest>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddConditionWithClinicLocalTodayReturns201WhileUtcDateIsPreviousDay()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var expected = new ChronicConditionResponse
        {
            ConditionId = Guid.NewGuid(),
            ConditionName = "Hypertension",
            DateDiagnosed = PatientServiceWebApplicationFactory.FixedClinicToday
        };
        factory.ChronicConditionServiceMock
            .Setup(service => service.AddConditionAsync(
                patientId,
                It.Is<ChronicConditionRequest>(request =>
                    request.DateDiagnosed == PatientServiceWebApplicationFactory.FixedClinicToday),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync(
            $"/api/patients/{patientId}/conditions",
            new
            {
                ConditionName = "Hypertension",
                DateDiagnosed = PatientServiceWebApplicationFactory.FixedClinicToday.ToString("yyyy-MM-dd")
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        factory.ChronicConditionServiceMock.VerifyAll();
    }

    [Fact]
    public async Task AddConditionWithFutureDiagnosedDateReturns400WithExactMessage()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync(
            $"/api/patients/{patientId}/conditions",
            new
            {
                ConditionName = "Hypertension",
                DateDiagnosed = PatientServiceWebApplicationFactory.FixedClinicToday.AddDays(1).ToString("yyyy-MM-dd")
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("Diagnosed date cannot be in the future", errors["DateDiagnosed"]);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task AddConditionAsNonReceptionistReturns403(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, role);

        var response = await client.PostAsJsonAsync(
            $"/api/patients/{patientId}/conditions",
            ValidConditionBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task GetConditionsReturnsRecordedConditions(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var expected = SampleCondition();
        factory.ChronicConditionServiceMock
            .Setup(service => service.GetConditionsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);
        var client = CreateClientWithRole(factory, role);

        var response = await client.GetAsync($"/api/patients/{patientId}/conditions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ChronicConditionResponse>>();
        Assert.Equal(expected.ConditionName, Assert.Single(body!).ConditionName);
    }

    [Fact]
    public async Task GetConditionsForPatientWithNoneReturnsEmptyArray()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.ChronicConditionServiceMock
            .Setup(service => service.GetConditionsAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.GetAsync($"/api/patients/{patientId}/conditions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<ChronicConditionResponse>>())!);
    }

    [Fact]
    public async Task RemoveConditionAsReceptionistReturns204()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var conditionId = Guid.NewGuid();
        factory.ChronicConditionServiceMock
            .Setup(service => service.RemoveConditionAsync(
                patientId,
                conditionId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.DeleteAsync(
            $"/api/patients/{patientId}/conditions/{conditionId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveConditionAsDoctorReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var conditionId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Doctor");

        var response = await client.DeleteAsync(
            $"/api/patients/{patientId}/conditions/{conditionId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveUnknownConditionReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var conditionId = Guid.NewGuid();
        factory.ChronicConditionServiceMock
            .Setup(service => service.RemoveConditionAsync(
                patientId,
                conditionId,
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.DeleteAsync(
            $"/api/patients/{patientId}/conditions/{conditionId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConditionsEndpointWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.GetAsync($"/api/patients/{Guid.NewGuid()}/conditions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorsAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errorsElement = document.RootElement.GetProperty("errors");

        var result = new Dictionary<string, string[]>();
        foreach (var property in errorsElement.EnumerateObject())
        {
            result[property.Name] = property.Value
                .EnumerateArray()
                .Select(element => element.GetString()!)
                .ToArray();
        }

        return result;
    }
}
