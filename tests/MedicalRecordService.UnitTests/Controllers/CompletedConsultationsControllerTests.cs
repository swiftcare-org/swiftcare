using MedicalRecordService.Controllers;
using MedicalRecordService.Models.Dtos;
using MedicalRecordService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public class CompletedConsultationsControllerTests
{
    [Fact]
    public async Task PageUsesTrustedDoctorIdentity()
    {
        var doctor = Guid.NewGuid();
        var service = new Mock<IConsultationCompletionService>();
        IReadOnlyList<CompletedConsultationContextResponse> visits = [new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())];
        service.Setup(s => s.FindCompletedPageAsync(doctor, 1, It.IsAny<CancellationToken>())).ReturnsAsync(visits);
        var controller = Controller(service, "Doctor", doctor.ToString());
        var result = Assert.IsType<OkObjectResult>(await controller.Get(1));
        Assert.Same(visits, result.Value);
        service.VerifyAll();
    }

    [Theory]
    [InlineData("Admin", "11111111-1111-1111-1111-111111111111", 0, 403)]
    [InlineData("Doctor", "invalid", 0, 401)]
    [InlineData("Doctor", "00000000-0000-0000-0000-000000000000", 0, 401)]
    [InlineData("Doctor", "11111111-1111-1111-1111-111111111111", -1, 400)]
    [InlineData("Doctor", "11111111-1111-1111-1111-111111111111", int.MaxValue, 400)]
    public async Task InvalidRequestsDoNotReadVisits(string role, string user, int page, int status)
    {
        var service = new Mock<IConsultationCompletionService>(MockBehavior.Strict);
        var response = Assert.IsAssignableFrom<ObjectResult>(await Controller(service, role, user).Get(page));
        Assert.Equal(status, response.StatusCode);
    }

    private static CompletedConsultationsController Controller(Mock<IConsultationCompletionService> service, string role, string user)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Role"] = role;
        context.Request.Headers["X-User-Id"] = user;
        return new CompletedConsultationsController(service.Object) { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
