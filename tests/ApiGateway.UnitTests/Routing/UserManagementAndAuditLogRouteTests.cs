using System.Net;
using System.Net.Http.Headers;

namespace ApiGateway.UnitTests.Routing;

// SWC-138: gateway authorization for the account management and audit log routes.
public class UserManagementAndAuditLogRouteTests
{
    private const string UserPath = "/api/users/4d8f6f0e-3b0f-4b5a-9d6e-7f1c2a9b8c11";
    private const string DoctorsPath = "/api/users/doctors";
    private const string AuditLogPath = "/api/audit-logs";

    public static TheoryData<string, string> AdminOnlyRoutes => new()
    {
        { "PUT", UserPath },
        { "PUT", $"{UserPath}/reset-password" },
        { "PUT", $"{UserPath}/deactivate" },
        { "PUT", $"{UserPath}/activate" },
        { "GET", AuditLogPath }
    };

    private static async Task<HttpResponseMessage> SendAsync(string method, string path, string? role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", factory.CreateSignedToken(role: role));
        }

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        return await client.SendAsync(request);
    }

    // AuthService is not running here, so a proxied request fails downstream. These
    // assert only that the Gateway itself matched the route and let the role through.
    private static void AssertPassedGatewayAuthorization(HttpResponseMessage response)
    {
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task AdminOnlyRouteWithAnAdminTokenPassesGatewayAuthorization(string method, string path)
    {
        using var response = await SendAsync(method, path, "Admin");

        AssertPassedGatewayAuthorization(response);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task AdminOnlyRouteWithADoctorTokenReturns403(string method, string path)
    {
        using var response = await SendAsync(method, path, "Doctor");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task AdminOnlyRouteWithAReceptionistTokenReturns403(string method, string path)
    {
        using var response = await SendAsync(method, path, "Receptionist");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyRoutes))]
    public async Task AdminOnlyRouteWithoutABearerTokenReturns401(string method, string path)
    {
        using var response = await SendAsync(method, path, role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Receptionist")]
    [InlineData("Doctor")]
    public async Task DoctorsRouteIsOpenToEveryStaffRole(string role)
    {
        using var response = await SendAsync("GET", DoctorsPath, role);

        AssertPassedGatewayAuthorization(response);
    }

    [Fact]
    public async Task DoctorsRouteWithoutABearerTokenReturns401()
    {
        using var response = await SendAsync("GET", DoctorsPath, role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The audit log is read-only and accounts are never hard deleted, so the Gateway
    // exposes no route for these at all.
    [Theory]
    [InlineData("POST", AuditLogPath)]
    [InlineData("PUT", AuditLogPath)]
    [InlineData("DELETE", AuditLogPath)]
    [InlineData("DELETE", UserPath)]
    public async Task UnsupportedMethodHasNoGatewayRoute(string method, string path)
    {
        using var response = await SendAsync(method, path, "Admin");

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
