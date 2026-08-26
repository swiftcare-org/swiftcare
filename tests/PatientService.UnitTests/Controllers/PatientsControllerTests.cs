using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using PatientService.Models.Dtos;
using PatientService.Models.Enums;
using PatientService.Services;

namespace PatientService.UnitTests.Controllers;

public class PatientsControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

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
