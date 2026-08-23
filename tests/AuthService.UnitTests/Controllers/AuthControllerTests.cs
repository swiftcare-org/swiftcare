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

    [Fact]
    public async Task LogoutWithValidGatewaySecretAndUserIdReturns204AndInvokesService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var userId = Guid.NewGuid();

        factory.AuthenticationServiceMock
            .Setup(s => s.LogoutAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        client.DefaultRequestHeaders.Add("X-User-Id", userId.ToString());

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        factory.AuthenticationServiceMock.Verify(
            s => s.LogoutAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LogoutWithoutGatewaySecretHeaderReturns401AndNeverInvokesService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", Guid.NewGuid().ToString());

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.AuthenticationServiceMock.Verify(
            s => s.LogoutAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task LogoutWithoutUserIdHeaderReturns401WithExactMessageAndNeverInvokesService()
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
        factory.AuthenticationServiceMock.Verify(
            s => s.LogoutAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-00000000000Z")]
    public async Task LogoutWithMalformedUserIdHeaderReturns401AndNeverInvokesService(string userIdHeaderValue)
    {
        using var factory = new AuthServiceWebApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(GatewaySecretHeaderName, AuthServiceWebApplicationFactory.ValidGatewaySecret);
        if (userIdHeaderValue.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-User-Id", userIdHeaderValue);
        }

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        factory.AuthenticationServiceMock.Verify(
            s => s.LogoutAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
