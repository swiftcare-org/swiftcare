using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using PatientService.Models.Dtos;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Controllers;

public class PatientsControllerTests
{
    // Mirrors the JsonStringEnumConverter registered in Program.cs - ReadFromJsonAsync with
    // default options would fail to parse "A+" back into the BloodGroup enum.
    private static readonly JsonSerializerOptions StringEnumOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    private static object ValidRegisterPatientBody() => new
    {
        Nic = "199012345678",
        FullName = "Test Patient",
        DateOfBirth = "1990-04-17",
        Gender = "Male",
        Address = "123 Test Road, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = "A+"
    };

    private static object ValidUpdatePatientBody() => new
    {
        Address = "456 Updated Road, Colombo",
        PhoneNumber = "0777654321",
        BloodGroup = "O-"
    };

    private static HttpClient CreateClientWithRole(PatientServiceWebApplicationFactory factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, PatientServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        client.DefaultRequestHeaders.Add(UserIdHeaderName, Guid.NewGuid().ToString());
        return client;
    }

    [Theory]
    [InlineData("Receptionist")]
    public async Task RegisterPatientWithValidRequestReturns201WithNoPhi(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var expectedPatientId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        factory.PatientRegistrationServiceMock
            .Setup(s => s.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPatientResult
            {
                Outcome = RegisterPatientOutcome.Success,
                Patient = new RegisteredPatientResponse { PatientId = expectedPatientId, CreatedAt = createdAt }
            });

        var client = CreateClientWithRole(factory, role);
        var response = await client.PostAsJsonAsync("/api/patients", ValidRegisterPatientBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fullName", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nic", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bloodGroup", rawBody, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadFromJsonAsync<RegisteredPatientResponse>();
        Assert.NotNull(body);
        Assert.Equal(expectedPatientId, body!.PatientId);
    }

    [Fact]
    public async Task RegisterPatientWithMissingRequiredFieldsReturns400WithPerFieldErrors()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PostAsJsonAsync("/api/patients", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.True(errors.ContainsKey("Nic"));
        Assert.True(errors.ContainsKey("FullName"));
        Assert.True(errors.ContainsKey("DateOfBirth"));
        Assert.True(errors.ContainsKey("Gender"));
        Assert.True(errors.ContainsKey("Address"));
        Assert.True(errors.ContainsKey("PhoneNumber"));
        Assert.True(errors.ContainsKey("BloodGroup"));
        factory.PatientRegistrationServiceMock.Verify(
            s => s.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterPatientWithInvalidBloodGroupReturns400()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Receptionist");

        var body = new
        {
            Nic = "199012345678",
            FullName = "Test Patient",
            DateOfBirth = "1990-04-17",
            Gender = "Male",
            Address = "123 Test Road, Colombo",
            PhoneNumber = "0771234567",
            BloodGroup = "Z+"
        };

        var response = await client.PostAsJsonAsync("/api/patients", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.PatientRegistrationServiceMock.Verify(
            s => s.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterPatientWithDuplicateNicReturns400WithExactAcMessage()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        factory.PatientRegistrationServiceMock
            .Setup(s => s.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterPatientResult { Outcome = RegisterPatientOutcome.DuplicateNic });

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.PostAsJsonAsync("/api/patients", ValidRegisterPatientBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal(
            "A patient with this NIC already exists. Please search for the existing patient.",
            errors["Nic"][0]);
    }

    [Fact]
    public async Task RegisterPatientWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.PostAsJsonAsync("/api/patients", ValidRegisterPatientBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.PatientRegistrationServiceMock.Verify(
            s => s.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterPatientWithDoctorRoleHeaderReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Doctor");

        var response = await client.PostAsJsonAsync("/api/patients", ValidRegisterPatientBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
        factory.PatientRegistrationServiceMock.Verify(
            s => s.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterPatientWithAdminRoleHeaderReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Admin");

        var response = await client.PostAsJsonAsync("/api/patients", ValidRegisterPatientBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
        factory.PatientRegistrationServiceMock.Verify(
            s => s.RegisterPatientAsync(
                It.IsAny<RegisterPatientRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterPatientWithMissingRoleHeaderReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, PatientServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.PostAsJsonAsync("/api/patients", ValidRegisterPatientBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static PatientSearchResultResponse SampleSearchResult() => new()
    {
        PatientId = Guid.NewGuid(),
        FullName = "Test Patient",
        Nic = "199012345678",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive
    };

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task SearchPatientsWithAuthorizedRoleReturns200WithResults(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var expected = SampleSearchResult();
        factory.PatientSearchServiceMock
            .Setup(s => s.SearchPatientsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([expected]);

        var client = CreateClientWithRole(factory, role);
        var response = await client.GetAsync("/api/patients/search?q=Test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<PatientSearchResultResponse>>(StringEnumOptions);
        Assert.NotNull(body);
        Assert.Single(body!);
        Assert.Equal(expected.PatientId, body![0].PatientId);
    }

    [Fact]
    public async Task SearchPatientsWithNoMatchesReturns200WithEmptyArray()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        factory.PatientSearchServiceMock
            .Setup(s => s.SearchPatientsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.GetAsync("/api/patients/search?q=nobody-matches-this");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<PatientSearchResultResponse>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    [Fact]
    public async Task SearchPatientsWithoutQueryParameterReturns200WithEmptyArray()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        factory.PatientSearchServiceMock
            .Setup(s => s.SearchPatientsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.GetAsync("/api/patients/search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<PatientSearchResultResponse>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }

    [Fact]
    public async Task SearchPatientsWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.GetAsync("/api/patients/search?q=Test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.PatientSearchServiceMock.Verify(
            s => s.SearchPatientsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchPatientsWithUnknownRoleReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var client = CreateClientWithRole(factory, "Nurse");

        var response = await client.GetAsync("/api/patients/search?q=Test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
        factory.PatientSearchServiceMock.Verify(
            s => s.SearchPatientsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchPatientsResponseContainsNoUnnecessaryPhi()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        factory.PatientSearchServiceMock
            .Setup(s => s.SearchPatientsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([SampleSearchResult()]);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.GetAsync("/api/patients/search?q=Test");

        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("dateOfBirth", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gender", rawBody, StringComparison.OrdinalIgnoreCase);
    }

    private static PatientProfileResponse SampleProfile(Guid patientId) => new()
    {
        PatientId = patientId,
        FullName = "Test Patient",
        Nic = "199012345678",
        DateOfBirth = new DateOnly(1990, 4, 17),
        Gender = Gender.Male,
        Address = "123 Test Road, Colombo",
        PhoneNumber = "0771234567",
        BloodGroup = BloodGroup.APositive,
        RegisteredAt = DateTime.UtcNow
    };

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task GetPatientWithAuthorizedRoleReturns200(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.PatientProfileServiceMock
            .Setup(s => s.GetPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleProfile(patientId));

        var client = CreateClientWithRole(factory, role);
        var response = await client.GetAsync($"/api/patients/{patientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PatientProfileResponse>(StringEnumOptions);
        Assert.Equal(patientId, body!.PatientId);
    }

    [Fact]
    public async Task GetPatientForUnknownIdReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.PatientProfileServiceMock
            .Setup(s => s.GetPatientAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatientProfileResponse?)null);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.GetAsync($"/api/patients/{patientId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPatientWithUnknownRoleReturns403()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Nurse");

        var response = await client.GetAsync($"/api/patients/{patientId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPatientWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.GetAsync($"/api/patients/{patientId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CheckInExistingPatientAsReceptionistReturns202()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        const string correlationId = "check-in-correlation-id";
        factory.PatientCheckInServiceMock
            .Setup(service => service.CheckInPatientAsync(
                patientId,
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckInPatientOutcome.Success);

        var client = CreateClientWithRole(factory, "Receptionist");
        client.DefaultRequestHeaders.Add(CorrelationIdHeaderName, correlationId);
        var response = await client.PostAsync($"/api/patients/{patientId}/check-in", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Patient check-in accepted", body!.Message);
        factory.PatientCheckInServiceMock.Verify(
            service => service.CheckInPatientAsync(
                patientId,
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckInUnknownPatientReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.PatientCheckInServiceMock
            .Setup(service => service.CheckInPatientAsync(
                patientId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckInPatientOutcome.PatientNotFound);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.PostAsync($"/api/patients/{patientId}/check-in", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Patient not found", body!.Message);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task CheckInPatientWithNonReceptionistRoleReturns403(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, role);

        var response = await client.PostAsync($"/api/patients/{patientId}/check-in", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.PatientCheckInServiceMock.Verify(
            service => service.CheckInPatientAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckInPatientWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.PostAsync($"/api/patients/{patientId}/check-in", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.PatientCheckInServiceMock.Verify(
            service => service.CheckInPatientAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckInPatientWhenEventPublishFailsReturns503()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.PatientCheckInServiceMock
            .Setup(service => service.CheckInPatientAsync(
                patientId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckInPatientOutcome.EventPublishFailed);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.PostAsync($"/api/patients/{patientId}/check-in", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePatientWithValidRequestReturns200WithUpdatedProfile()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var expected = new PatientProfileResponse
        {
            PatientId = patientId,
            FullName = "Test Patient",
            Nic = "199012345678",
            DateOfBirth = new DateOnly(1990, 4, 17),
            Gender = Gender.Male,
            Address = "456 Updated Road, Colombo",
            PhoneNumber = "0777654321",
            BloodGroup = BloodGroup.ONegative,
            RegisteredAt = DateTime.UtcNow
        };
        factory.PatientProfileServiceMock
            .Setup(s => s.UpdatePatientAsync(
                patientId, It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var client = CreateClientWithRole(factory, "Receptionist");
        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}", ValidUpdatePatientBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PatientProfileResponse>(StringEnumOptions);
        Assert.NotNull(body);
        Assert.Equal("456 Updated Road, Colombo", body!.Address);
        Assert.Equal("0777654321", body.PhoneNumber);
        Assert.Equal(BloodGroup.ONegative, body.BloodGroup);
    }

    [Fact]
    public async Task UpdatePatientWithMissingRequiredFieldsReturns400()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.True(errors.ContainsKey("Address"));
        Assert.True(errors.ContainsKey("PhoneNumber"));
        Assert.True(errors.ContainsKey("BloodGroup"));
        factory.PatientProfileServiceMock.Verify(
            s => s.UpdatePatientAsync(
                It.IsAny<Guid>(), It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdatePatientForUnknownIdReturns404()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        factory.PatientProfileServiceMock
            .Setup(s => s.UpdatePatientAsync(
                patientId, It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PatientProfileResponse?)null);
        var client = CreateClientWithRole(factory, "Receptionist");

        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}", ValidUpdatePatientBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task UpdatePatientWithNonReceptionistRoleReturns403(string role)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = CreateClientWithRole(factory, role);

        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}", ValidUpdatePatientBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.PatientProfileServiceMock.Verify(
            s => s.UpdatePatientAsync(
                It.IsAny<Guid>(), It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdatePatientWithoutGatewaySecretReturns401()
    {
        using var factory = new PatientServiceWebApplicationFactory();
        var patientId = Guid.NewGuid();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.PutAsJsonAsync($"/api/patients/{patientId}", ValidUpdatePatientBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.PatientProfileServiceMock.Verify(
            s => s.UpdatePatientAsync(
                It.IsAny<Guid>(), It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
