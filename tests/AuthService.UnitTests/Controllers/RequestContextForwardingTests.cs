using System.ComponentModel.DataAnnotations;
using System.Net;
using AuthService.Controllers;
using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AuthService.UnitTests.Controllers;

// The correlation ID and client IP recorded in the audit log are taken from the request,
// with documented fallbacks when they are missing (SWC-151 mutation testing).
public class RequestContextForwardingTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task LoginForwardsTheCallersCorrelationIdAndIpAddress()
    {
        var captured = new Captured();
        var controller = new AuthController(LoginService(captured).Object)
        {
            ControllerContext = Context(correlationId: "corr-123", ipAddress: "10.0.0.5")
        };

        await controller.Login(new LoginRequest { Username = "dr.chen", Password = "secret" }, CancellationToken.None);

        Assert.Equal("corr-123", captured.CorrelationId);
        Assert.Equal("10.0.0.5", captured.IpAddress);
    }

    [Fact]
    public async Task LoginFallsBackToANewCorrelationIdAndUnknownIp()
    {
        var captured = new Captured();
        var controller = new AuthController(LoginService(captured).Object)
        {
            ControllerContext = Context(correlationId: null, ipAddress: null)
        };

        await controller.Login(new LoginRequest { Username = "dr.chen", Password = "secret" }, CancellationToken.None);

        Assert.True(Guid.TryParse(captured.CorrelationId, out _));
        Assert.Equal("unknown", captured.IpAddress);
    }

    [Theory]
    [InlineData("corr-123", "10.0.0.5", "corr-123", "10.0.0.5")]
    [InlineData(null, null, null, "unknown")]
    public async Task LogoutForwardsCorrelationIdAndIpAddressWithFallbacks(
        string? correlationId,
        string? ipAddress,
        string? expectedCorrelationId,
        string expectedIpAddress)
    {
        var captured = new Captured();
        var service = new Mock<IAuthenticationService>();
        service.Setup(candidate => candidate.LogoutAsync(UserId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, CancellationToken>((_, correlation, ip, _) =>
            {
                captured.CorrelationId = correlation;
                captured.IpAddress = ip;
            })
            .Returns(Task.CompletedTask);
        var context = Context(correlationId, ipAddress);
        context.HttpContext.Request.Headers["X-User-Id"] = UserId.ToString();

        var result = await new AuthController(service.Object) { ControllerContext = context }
            .Logout(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(expectedIpAddress, captured.IpAddress);
        if (expectedCorrelationId is null)
        {
            Assert.True(Guid.TryParse(captured.CorrelationId, out _));
        }
        else
        {
            Assert.Equal(expectedCorrelationId, captured.CorrelationId);
        }
    }

    [Theory]
    [InlineData("corr-123")]
    [InlineData(null)]
    public async Task CreateUserForwardsTheCorrelationIdOrANewOne(string? correlationId)
    {
        string? forwarded = null;
        var service = new Mock<IUserAccountService>();
        service.Setup(candidate => candidate.CreateUserAsync(
                It.IsAny<CreateUserRequest>(),
                It.IsAny<AdminActionContext>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateUserRequest, AdminActionContext, CancellationToken>(
                (_, actionContext, _) => forwarded = actionContext.CorrelationId)
            .ReturnsAsync(new CreateUserResult { Outcome = CreateUserOutcome.Success });
        var context = Context(correlationId, ipAddress: null);
        context.HttpContext.Request.Headers["X-User-Role"] = "Admin";

        await new UsersController(service.Object, NullLogger<UsersController>.Instance) { ControllerContext = context }
            .CreateUser(new CreateUserRequest(), CancellationToken.None);

        if (correlationId is null)
        {
            Assert.True(Guid.TryParse(forwarded, out _));
        }
        else
        {
            Assert.Equal(correlationId, forwarded);
        }
    }

    [Fact]
    public void EmptyLoginRequestFailsBothRequiredFields()
    {
        Assert.Equal(
            ["Password is required.", "Username is required."],
            ValidationMessages(new LoginRequest()));
    }

    [Fact]
    public void EmptyCreateUserRequestFailsEveryRequiredField()
    {
        var messages = ValidationMessages(new CreateUserRequest());

        Assert.Contains("Username is required.", messages);
        Assert.Contains("Password is required.", messages);
        Assert.Contains("Full name is required.", messages);
    }

    private static List<string?> ValidationMessages(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results.Select(result => result.ErrorMessage).Order().ToList();
    }

    private static Mock<IAuthenticationService> LoginService(Captured captured)
    {
        var service = new Mock<IAuthenticationService>();
        service.Setup(candidate => candidate.LoginAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((_, _, correlation, ip, _) =>
            {
                captured.CorrelationId = correlation;
                captured.IpAddress = ip;
            })
            .ReturnsAsync(new LoginResult { Outcome = LoginOutcome.InvalidCredentials });
        return service;
    }

    private static ControllerContext Context(string? correlationId, string? ipAddress)
    {
        var context = new DefaultHttpContext();
        if (correlationId is not null)
        {
            context.Request.Headers["X-Correlation-ID"] = correlationId;
        }

        if (ipAddress is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        }

        return new ControllerContext { HttpContext = context };
    }

    private sealed class Captured
    {
        public string? CorrelationId { get; set; }

        public string? IpAddress { get; set; }
    }
}
