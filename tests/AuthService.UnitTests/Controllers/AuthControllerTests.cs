using System.Net;
using System.Net.Http.Json;
using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using AuthService.Services;
using Moq;

namespace AuthService.UnitTests.Controllers;

public class AuthControllerTests
{
    private const string GatewaySecretHeaderName = "X-Gateway-Secret";

    [Fact]
    public async Task LoginWithValidCredentialsReturns200WithTokenAndUserPayload()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var expectedUserId = Guid.NewGuid();
        var expectedExpiry = DateTime.UtcNow.AddHours(12);

        factory.AuthenticationServiceMock
            .Setup(s => s.LoginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult
            {
                Outcome = LoginOutcome.Success,
                Token = "signed-jwt",
                ExpiresAt = expectedExpiry,
                User = new AuthenticatedUser
                {
                    UserId = expectedUserId,
                    FullName = "Dr. Amara Chen",
                    Role = UserRole.Doctor,
                    RoomNumber = "R-204"
                }
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "dr.chen", Password = "correct-password" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.Equal("signed-jwt", body!.Token);
        Assert.Equal(expectedUserId, body.User.UserId);
        Assert.Equal("Dr. Amara Chen", body.User.FullName);
        Assert.Equal(nameof(UserRole.Doctor), body.User.Role);
        Assert.Equal("R-204", body.User.RoomNumber);
    }

    [Fact]
    public async Task LoginWithInvalidCredentialsReturns401WithExactMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        factory.AuthenticationServiceMock
            .Setup(s => s.LoginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult { Outcome = LoginOutcome.InvalidCredentials });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "dr.chen", Password = "wrong-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Invalid username or password", body!.Message);
    }

    [Fact]
    public async Task LoginWithDeactivatedAccountReturns403WithExactMessage()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        factory.AuthenticationServiceMock
            .Setup(s => s.LoginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResult { Outcome = LoginOutcome.AccountDeactivated });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "dr.rao", Password = "correct-password" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Your account has been deactivated. Contact your administrator.", body!.Message);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("   ", "password")]
    [InlineData("username", "")]
    [InlineData("username", "   ")]
    public async Task LoginWithEmptyOrWhitespaceFieldsReturns400AndNeverInvokesService(string username, string password)
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = username, Password = password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        factory.AuthenticationServiceMock.Verify(
            s => s.LoginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginWithoutGatewaySecretHeaderReturns401AndNeverInvokesService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "dr.chen", Password = "correct-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.AuthenticationServiceMock.Verify(
            s => s.LoginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LoginWithInvalidGatewaySecretHeaderReturns401AndNeverInvokesService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, "not-the-real-secret");

        var response = await client.PostAsJsonAsync("/api/auth/login", new { Username = "dr.chen", Password = "correct-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.AuthenticationServiceMock.Verify(
            s => s.LoginAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
