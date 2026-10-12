using AuthService.Controllers;
using AuthService.Models.Dtos;
using AuthService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AuthService.UnitTests.Controllers;

// The message of the audit log's 403 response (SWC-151 mutation testing).
public class AuditLogsControllerEdgeCaseTests
{
    [Theory]
    [InlineData("Doctor")]
    [InlineData(null)]
    public async Task NonAdminGets403WithTheForbiddenMessage(string? role)
    {
        var service = new Mock<IAuditLogService>(MockBehavior.Strict);
        var httpContext = new DefaultHttpContext();
        if (role is not null)
        {
            httpContext.Request.Headers["X-User-Role"] = role;
        }

        var controller = new AuditLogsController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = Assert.IsType<ObjectResult>(await controller.GetAuditLog(null, CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal("Forbidden", Assert.IsType<MessageResponse>(result.Value).Message);
        service.VerifyNoOtherCalls();
    }
}
