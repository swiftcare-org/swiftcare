using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace MedicalRecordService.UnitTests.Controllers;

public class CompletedConsultationsControllerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue / 50)]
    public async Task PageUsesTrustedDoctorIdentity(int page)
    {
        var doctor = Guid.NewGuid();
        var service = new Mock<IConsultationCompletionService>();
        IReadOnlyList<CompletedConsultationContextResponse> visits = [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())];
        service.Setup(s => s.FindCompletedPageAsync(doctor, page, It.IsAny<CancellationToken>())).ReturnsAsync(visits);
        var controller = Controller(service, "Doctor", doctor.ToString());
        controller.Request.Headers["X-User-Role"] = "Admin";
        controller.Request.Headers["X-User-Id"] = Guid.NewGuid().ToString();
        var result = Assert.IsType<OkObjectResult>(await controller.Get(page));
        Assert.Same(visits, result.Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Admin", "11111111-1111-1111-1111-111111111111", 0, 403, "Forbidden")]
    [InlineData(null, "11111111-1111-1111-1111-111111111111", 0, 403, "Forbidden")]
    [InlineData("Doctor", null, 0, 401, "Doctor identity is unavailable")]
    [InlineData("Doctor", "invalid", 0, 401, "Doctor identity is unavailable")]
    [InlineData("Doctor", "00000000-0000-0000-0000-000000000000", 0, 401, "Doctor identity is unavailable")]
    [InlineData("Doctor", "11111111-1111-1111-1111-111111111111", -1, 400, "Page is outside the supported range")]
    [InlineData("Doctor", "11111111-1111-1111-1111-111111111111", int.MaxValue / 50 + 1, 400, "Page is outside the supported range")]
    [InlineData("Doctor", "11111111-1111-1111-1111-111111111111", int.MaxValue, 400, "Page is outside the supported range")]
    public async Task InvalidRequestsDoNotReadVisits(string? role, string? user, int page, int status, string message)
    {
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        var response = Assert.IsAssignableFrom<ObjectResult>(await Controller(service, role, user).Get(page));
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(message, Assert.IsType<MessageResponse>(response.Value).Message);
        service.VerifyNoOtherCalls();
    }

    private static CompletedConsultationsController Controller(Mock<IConsultationCompletionService> service, string? role, string? user)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        context.Request.Headers["X-User-Id"] = user;
        var claims = new List<Claim>();
        if (role is not null) claims.Add(new Claim(ClaimTypes.Role, role));
        if (user is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, user));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Gateway"));
        return new CompletedConsultationsController(service.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
