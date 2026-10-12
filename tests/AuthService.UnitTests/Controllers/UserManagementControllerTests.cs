using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using Moq;

namespace AuthService.UnitTests.Controllers;

// SWC-138: the account management endpoints added to /api/users by SWC-10.
public class UserManagementControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";
    private const string UserRoleHeaderName = "X-User-Role";
    private const string UserIdHeaderName = "X-User-Id";
    private const string EmptyId = "00000000-0000-0000-0000-000000000000";

    private static readonly Guid AdminUserId = Guid.NewGuid();
    private static readonly Guid TargetUserId = Guid.NewGuid();

    private static HttpClient CreateClient(AuthServiceWebApplicationFactory factory, string? role = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add(UserIdHeaderName, AdminUserId.ToString());
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(UserRoleHeaderName, role);
        }

        return client;
    }

    private static UserActionResult Outcome(UserActionOutcome outcome) => new() { Outcome = outcome };

    private static UserActionResult Success(bool isActive = true) => new()
    {
        Outcome = UserActionOutcome.Success,
        User = new UserSummaryResponse
        {
            UserId = TargetUserId,
            Username = "dr.chen",
            FullName = "Dr. Amara Perera",
            Role = "Doctor",
            RoomNumber = "R-310",
            Specialization = "Cardiology",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow
        }
    };

    private static async Task<Dictionary<string, string[]>> ReadValidationErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(e => e.GetString()!).ToArray());
    }

    private static object ValidUpdateBody() => new
    {
        FullName = "Dr. Amara Perera",
        RoomNumber = "R-310",
        Specialization = "Cardiology"
    };

    private static void SetupUpdate(AuthServiceWebApplicationFactory factory, UserActionResult result) =>
        factory.UserAccountServiceMock
            .Setup(s => s.UpdateUserAsync(
                It.IsAny<Guid>(), It.IsAny<UpdateUserRequest>(), It.IsAny<AdminActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task UpdateUserReturns200WithTheSavedDetailsAndNoPassword()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupUpdate(factory, Success());

        var response = await CreateClient(factory).PutAsJsonAsync($"/api/users/{TargetUserId}", ValidUpdateBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", rawBody, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadFromJsonAsync<UserSummaryResponse>();
        Assert.Equal("Dr. Amara Perera", body!.FullName);
        Assert.Equal("Cardiology", body.Specialization);
    }

    [Fact]
    public async Task UpdateUserPassesTheRouteIdAndActingAdminToTheService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupUpdate(factory, Success());

        await CreateClient(factory).PutAsJsonAsync($"/api/users/{TargetUserId}", ValidUpdateBody());

        factory.UserAccountServiceMock.Verify(
            s => s.UpdateUserAsync(
                TargetUserId,
                It.Is<UpdateUserRequest>(r => r.FullName == "Dr. Amara Perera" && r.RoomNumber == "R-310"),
                It.Is<AdminActionContext>(c => c.AdminUserId == AdminUserId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateUnknownUserReturns404()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupUpdate(factory, Outcome(UserActionOutcome.NotFound));

        var response = await CreateClient(factory).PutAsJsonAsync($"/api/users/{TargetUserId}", ValidUpdateBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("User not found", body!.Message);
    }

    [Fact]
    public async Task UpdateDoctorWithoutRoomNumberReturns400WithExactRoomNumberMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupUpdate(factory, Outcome(UserActionOutcome.RoomNumberRequiredForDoctor));

        var response = await CreateClient(factory).PutAsJsonAsync($"/api/users/{TargetUserId}", ValidUpdateBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal("Room number is required for doctors", errors["RoomNumber"][0]);
    }

    [Fact]
    public async Task UpdateUserWithoutFullNameReturns400AndNeverCallsTheService()
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory).PutAsJsonAsync(
            $"/api/users/{TargetUserId}", new { FullName = "", RoomNumber = "R-310" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.True(errors.ContainsKey("FullName"));
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpdateUserWithTheAllZeroIdReturns400AndNeverCallsTheService()
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory).PutAsJsonAsync($"/api/users/{EmptyId}", ValidUpdateBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("User ID must be provided.", body!.Message);
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData(null)]
    public async Task UpdateUserWithoutTheAdminRoleReturns403AndNeverCallsTheService(string? role)
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory, role).PutAsJsonAsync($"/api/users/{TargetUserId}", ValidUpdateBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    private static void SetupResetPassword(AuthServiceWebApplicationFactory factory, UserActionResult result) =>
        factory.UserAccountServiceMock
            .Setup(s => s.ResetPasswordAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<AdminActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task ResetPasswordReturns200AndNeverEchoesThePassword()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupResetPassword(factory, Success());

        var response = await CreateClient(factory).PutAsJsonAsync(
            $"/api/users/{TargetUserId}/reset-password", new { NewPassword = "a-brand-new-password" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("a-brand-new-password", rawBody);
        Assert.DoesNotContain("password", rawBody, StringComparison.OrdinalIgnoreCase);
        factory.UserAccountServiceMock.Verify(
            s => s.ResetPasswordAsync(
                TargetUserId,
                "a-brand-new-password",
                It.Is<AdminActionContext>(c => c.AdminUserId == AdminUserId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResetPasswordThatIsTooShortReturns400WithExactPasswordMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupResetPassword(factory, Outcome(UserActionOutcome.PasswordTooShort));

        var response = await CreateClient(factory).PutAsJsonAsync(
            $"/api/users/{TargetUserId}/reset-password", new { NewPassword = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.Equal("Password must be at least 8 characters", errors["NewPassword"][0]);
    }

    [Fact]
    public async Task ResetPasswordWithoutAPasswordReturns400AndNeverCallsTheService()
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory).PutAsJsonAsync(
            $"/api/users/{TargetUserId}/reset-password", new { NewPassword = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadValidationErrorsAsync(response);
        Assert.True(errors.ContainsKey("NewPassword"));
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResetPasswordForUnknownUserReturns404()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupResetPassword(factory, Outcome(UserActionOutcome.NotFound));

        var response = await CreateClient(factory).PutAsJsonAsync(
            $"/api/users/{TargetUserId}/reset-password", new { NewPassword = "a-brand-new-password" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ResetPasswordWithTheAllZeroIdReturns400AndNeverCallsTheService()
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory).PutAsJsonAsync(
            $"/api/users/{EmptyId}/reset-password", new { NewPassword = "a-brand-new-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData(null)]
    public async Task ResetPasswordWithoutTheAdminRoleReturns403AndNeverCallsTheService(string? role)
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory, role).PutAsJsonAsync(
            $"/api/users/{TargetUserId}/reset-password", new { NewPassword = "a-brand-new-password" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    private static void SetupDeactivate(AuthServiceWebApplicationFactory factory, UserActionResult result) =>
        factory.UserAccountServiceMock
            .Setup(s => s.DeactivateUserAsync(
                It.IsAny<Guid>(), It.IsAny<AdminActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private static void SetupReactivate(AuthServiceWebApplicationFactory factory, UserActionResult result) =>
        factory.UserAccountServiceMock
            .Setup(s => s.ReactivateUserAsync(
                It.IsAny<Guid>(), It.IsAny<AdminActionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task DeactivateUserReturns200WithTheInactiveAccount()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupDeactivate(factory, Success(isActive: false));

        var response = await CreateClient(factory).PutAsync($"/api/users/{TargetUserId}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserSummaryResponse>();
        Assert.False(body!.IsActive);
        factory.UserAccountServiceMock.Verify(
            s => s.DeactivateUserAsync(
                TargetUserId,
                It.Is<AdminActionContext>(c => c.AdminUserId == AdminUserId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeactivateOwnAccountReturns400WithExactMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupDeactivate(factory, Outcome(UserActionOutcome.CannotDeactivateOwnAccount));

        var response = await CreateClient(factory).PutAsync($"/api/users/{AdminUserId}/deactivate", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("You cannot deactivate your own account", body!.Message);
    }

    [Fact]
    public async Task ReactivateUserReturns200WithTheActiveAccount()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupReactivate(factory, Success(isActive: true));

        var response = await CreateClient(factory).PutAsync($"/api/users/{TargetUserId}/activate", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UserSummaryResponse>();
        Assert.True(body!.IsActive);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("activate")]
    public async Task StatusChangeForUnknownUserReturns404(string action)
    {
        using var factory = new AuthServiceWebApplicationFactory();
        SetupDeactivate(factory, Outcome(UserActionOutcome.NotFound));
        SetupReactivate(factory, Outcome(UserActionOutcome.NotFound));

        var response = await CreateClient(factory).PutAsync($"/api/users/{TargetUserId}/{action}", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("activate")]
    public async Task StatusChangeWithTheAllZeroIdReturns400AndNeverCallsTheService(string action)
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory).PutAsync($"/api/users/{EmptyId}/{action}", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("deactivate", "Doctor")]
    [InlineData("deactivate", "Receptionist")]
    [InlineData("deactivate", null)]
    [InlineData("activate", "Doctor")]
    [InlineData("activate", "Receptionist")]
    [InlineData("activate", null)]
    public async Task StatusChangeWithoutTheAdminRoleReturns403AndNeverCallsTheService(string action, string? role)
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory, role).PutAsync($"/api/users/{TargetUserId}/{action}", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Receptionist")]
    [InlineData("Doctor")]
    public async Task GetDoctorsIsOpenToEveryStaffRole(string role)
    {
        using var factory = new AuthServiceWebApplicationFactory();
        factory.UserAccountServiceMock
            .Setup(s => s.GetActiveDoctorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new DoctorSummaryResponse
                {
                    UserId = TargetUserId,
                    FullName = "Dr. Amara Chen",
                    RoomNumber = "R-204",
                    Specialization = "Cardiology"
                }
            ]);

        var response = await CreateClient(factory, role).GetAsync("/api/users/doctors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doctors = await response.Content.ReadFromJsonAsync<List<DoctorSummaryResponse>>();
        Assert.Equal("Dr. Amara Chen", Assert.Single(doctors!).FullName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Patient")]
    [InlineData("admin")]
    [InlineData("0")]
    public async Task GetDoctorsWithoutAKnownStaffRoleReturns403AndNeverCallsTheService(string? role)
    {
        using var factory = new AuthServiceWebApplicationFactory();

        var response = await CreateClient(factory, role).GetAsync("/api/users/doctors");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        factory.UserAccountServiceMock.VerifyNoOtherCalls();
    }
}
