using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using PatientService.Models.Dtos;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Controllers;

public class AllergiesControllerTests
{
    private static readonly JsonSerializerOptions StringEnumOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private static HttpClient CreateClientWithRole(PatientServiceWebApplicationFactory factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, PatientServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        client.DefaultRequestHeaders.Add(UserIdHeaderName, Guid.NewGuid().ToString());
        return client;
    }

    private static object ValidAllergyBody() => new
    {
        AllergyName = "Penicillin",
        Severity = "Severe",
        Notes = "Causes rash"
    };

    private static AllergyResponse SampleAllergyResponse() => new()
    {
        AllergyId = Guid.NewGuid(),
        AllergyName = "Penicillin",
        Severity = AllergySeverity.Severe,
        Notes = "Causes rash",
        RecordedAt = DateTime.UtcNow
    };

    // --- POST ---

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    public async Task AddAllergyWithValidRequestReturns201(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var expected = SampleAllergyResponse();
        factory.AllergyServiceMock
            .Setup(s => s.AddAllergyAsync(patientId, It.IsAny<AllergyRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var client = CreateClientWithRole(factory, role);
        var response = await client.PostAsJsonAsync($"/api/patients/{patientId}/allergies", ValidAllergyBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AllergyResponse>(StringEnumOptions);
        Assert.Equal(expected.AllergyId, body!.AllergyId);
    }

    [Fact]
    public async Task AddAllergyWithEmptyNameReturns400WithExactMessageAndDoesNotCallTheService()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync($"/api/patients/{patientId}/allergies", new
        {
            AllergyName = "",
            Severity = "Severe"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("Allergy name is required", errors["AllergyName"]);
        factory.AllergyServiceMock.Verify(
            s => s.AddAllergyAsync(It.IsAny<Guid>(), It.IsAny<AllergyRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddAllergyWithMissingNameReturns400WithExactMessage()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync($"/api/patients/{patientId}/allergies", new { Severity = "Severe" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("Allergy name is required", errors["AllergyName"]);
    }

    [Fact]
    public async Task AddAllergyWithInvalidSeverityReturns400()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync($"/api/patients/{patientId}/allergies", new
        {
            AllergyName = "Penicillin",
            Severity = "Critical"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddAllergyForUnknownPatientReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.AllergyServiceMock
            .Setup(s => s.AddAllergyAsync(patientId, It.IsAny<AllergyRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AllergyResponse?)null);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.PostAsJsonAsync($"/api/patients/{patientId}/allergies", ValidAllergyBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddAllergyAsAdminReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Admin");

        var response = await client.PostAsJsonAsync($"/api/patients/{patientId}/allergies", ValidAllergyBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.AllergyServiceMock.Verify(
            s => s.AddAllergyAsync(It.IsAny<Guid>(), It.IsAny<AllergyRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddAllergyWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.PostAsJsonAsync($"/api/patients/{patientId}/allergies", ValidAllergyBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.AllergyServiceMock.Verify(
            s => s.AddAllergyAsync(It.IsAny<Guid>(), It.IsAny<AllergyRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // --- GET ---

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task GetAllergiesReturns200Ordered(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var severe = SampleAllergyResponse();
        factory.AllergyServiceMock
            .Setup(s => s.GetAllergiesAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([severe]);

        var client = CreateClientWithRole(factory, role);
        var response = await client.GetAsync($"/api/patients/{patientId}/allergies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<AllergyResponse>>(StringEnumOptions);
        Assert.Single(body!);
    }

    [Fact]
    public async Task GetAllergiesForPatientWithNoneReturns200WithEmptyArray()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.AllergyServiceMock
            .Setup(s => s.GetAllergiesAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.GetAsync($"/api/patients/{patientId}/allergies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<AllergyResponse>>();
        Assert.Empty(body!);
    }

    [Fact]
    public async Task GetAllergiesForUnknownPatientReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.AllergyServiceMock
            .Setup(s => s.GetAllergiesAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AllergyResponse>?)null);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.GetAsync($"/api/patients/{patientId}/allergies");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAllergiesWithUnknownRoleReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Nurse");

        var response = await client.GetAsync($"/api/patients/{patientId}/allergies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- PUT ---

    [Fact]
    public async Task UpdateAllergyReturns200()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        var updated = SampleAllergyResponse();
        factory.AllergyServiceMock
            .Setup(s => s.UpdateAllergyAsync(patientId, allergyId, It.IsAny<AllergyRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updated);

        var client = CreateClientWithRole(factory, "Doctor");
        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}/allergies/{allergyId}", ValidAllergyBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAllergyAsAdminReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Admin");

        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}/allergies/{allergyId}", ValidAllergyBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUnknownAllergyReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        factory.AllergyServiceMock
            .Setup(s => s.UpdateAllergyAsync(patientId, allergyId, It.IsAny<AllergyRequest>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AllergyResponse?)null);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}/allergies/{allergyId}", ValidAllergyBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAllergyWithEmptyNameReturns400()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}/allergies/{allergyId}", new
        {
            AllergyName = "",
            Severity = "Mild"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Contains("Allergy name is required", errors["AllergyName"]);
    }

    // --- DELETE ---

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    public async Task RemoveAllergyReturns204(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        factory.AllergyServiceMock
            .Setup(s => s.RemoveAllergyAsync(patientId, allergyId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var client = CreateClientWithRole(factory, role);
        var response = await client.DeleteAsync($"/api/patients/{patientId}/allergies/{allergyId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAllergyAsAdminReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Admin");

        var response = await client.DeleteAsync($"/api/patients/{patientId}/allergies/{allergyId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.AllergyServiceMock.Verify(
            s => s.RemoveAllergyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RemoveUnknownAllergyReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        factory.AllergyServiceMock
            .Setup(s => s.RemoveAllergyAsync(patientId, allergyId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.DeleteAsync($"/api/patients/{patientId}/allergies/{allergyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAllergyWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var allergyId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.DeleteAsync($"/api/patients/{patientId}/allergies/{allergyId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errorsElement = document.RootElement.GetProperty("errors");

        var result = new Dictionary<string, string[]>();
        foreach (var property in errorsElement.EnumerateObject())
        {
            result[property.Name] = property.Value.EnumerateArray().Select(e => e.GetString()!).ToArray();
        }

        return result;
    }
}
