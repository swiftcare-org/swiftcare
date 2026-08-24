using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using AuthService.Services;
using Moq;

namespace AuthService.UnitTests.Controllers;

public class UsersControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";

    private static object ValidCreateUserBody() => new
    {
        Username = "dr.new",
        Password = "correct-horse-battery-staple",
        FullName = "Dr. New Doctor",
        Role = "Doctor",
        RoomNumber = "R-301"
    };

    private static HttpClient CreateAdminClient(AuthServiceWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Admin");
        client.DefaultRequestHeaders.Add(UserIdHeaderName, Guid.NewGuid().ToString());
        return client;
    }

    [Fact]
    public async Task CreateUserWithValidRequestReturns201WithoutPasswordHash()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var expectedUserId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        factory.UserAccountServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateUserResult
            {
                Outcome = CreateUserOutcome.Success,
                User = new UserSummaryResponse
                {
                    UserId = expectedUserId,
                    Username = "dr.new",
                    FullName = "Dr. New Doctor",
                    Role = "Doctor",
                    RoomNumber = "R-301",
                    IsActive = true,
                    CreatedAt = createdAt
                }
            });

        var client = CreateAdminClient(factory);
        var response = await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", rawBody, StringComparison.OrdinalIgnoreCase);

        var body = await response.Content.ReadFromJsonAsync<UserSummaryResponse>();
        Assert.NotNull(body);
        Assert.Equal(expectedUserId, body!.UserId);
        Assert.Equal("dr.new", body.Username);
    }

    [Fact]
    public async Task CreateUserWithMissingRequiredFieldsReturns400WithPerFieldErrors()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync("/api/users", new { Username = "", Password = "", FullName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.True(errors.ContainsKey("Username"));
        Assert.True(errors.ContainsKey("Password"));
        Assert.True(errors.ContainsKey("FullName"));
        Assert.True(errors.ContainsKey("Role"));
        factory.UserAccountServiceMock.Verify(
            s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateUserWithMissingRoleReturns400()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = CreateAdminClient(factory);

        var response = await client.PostAsJsonAsync("/api/users", new
        {
            Username = "dr.new",
            Password = "correct-horse-battery-staple",
            FullName = "Dr. New Doctor"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.True(errors.ContainsKey("Role"));
        factory.UserAccountServiceMock.Verify(
            s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateUserWithDuplicateUsernameReturns400WithExactUsernameMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        factory.UserAccountServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateUserResult { Outcome = CreateUserOutcome.DuplicateUsername });

        var client = CreateAdminClient(factory);
        var response = await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal("Username already exists", errors["Username"][0]);
    }

    [Fact]
    public async Task CreateUserWithShortPasswordReturns400WithExactPasswordMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        factory.UserAccountServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateUserResult { Outcome = CreateUserOutcome.PasswordTooShort });

        var client = CreateAdminClient(factory);
        var response = await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal("Password must be at least 8 characters", errors["Password"][0]);
    }

    [Fact]
    public async Task CreateDoctorWithoutRoomNumberReturns400WithExactRoomNumberMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        factory.UserAccountServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreateUserResult { Outcome = CreateUserOutcome.RoomNumberRequiredForDoctor });

        var client = CreateAdminClient(factory);
        var response = await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal("Room number is required for doctors", errors["RoomNumber"][0]);
    }

    [Fact]
    public async Task CreateUserWithoutGatewaySecretReturns401()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Admin");

        var response = await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.UserAccountServiceMock.Verify(
            s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    public async Task CreateUserWithNonAdminRoleHeaderReturns403(string role)
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);

        var response = await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
    }

    [Fact]
    public async Task CreateUserWithMissingRoleHeaderReturns403()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUserRejectedByAuthorizationNeverCallsTheService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Doctor");

        await client.PostAsJsonAsync("/api/users", ValidCreateUserBody());

        factory.UserAccountServiceMock.Verify(
            s => s.CreateUserAsync(
                It.IsAny<CreateUserRequest>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUsersWithAdminRoleHeaderReturns200()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        factory.UserAccountServiceMock
            .Setup(s => s.GetUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var client = CreateAdminClient(factory);
        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUsersWithNonAdminRoleHeaderReturns403()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserRoleHeaderName, "Receptionist");

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.UserAccountServiceMock.Verify(s => s.GetUsersAsync(It.IsAny<CancellationToken>()), Times.Never);
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
