using System.Net;
using System.Net.Http.Headers;

namespace ApiGateway.UnitTests.Routing;

// SWC-146: gateway authorization for saving and removing consultation templates.
public class ConsultationTemplateRouteTests
{
    private const string TemplatesPath = "/api/templates";
    private const string TemplatePath = "/api/templates/4d8f6f0e-3b0f-4b5a-9d6e-7f1c2a9b8c11";

    public static TheoryData<string, string> DoctorOnlyRoutes => new()
    {
        { "POST", TemplatesPath },
        { "DELETE", TemplatePath }
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

    // MedicalRecordService is not running here, so a proxied request fails downstream. This
    // asserts only that the Gateway itself matched the route and let the doctor through.
    [Theory]
    [MemberData(nameof(DoctorOnlyRoutes))]
    public async Task RouteWithADoctorTokenPassesGatewayAuthorization(string method, string path)
    {
        using var response = await SendAsync(method, path, "Doctor");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(DoctorOnlyRoutes))]
    public async Task RouteWithAReceptionistTokenReturns403(string method, string path)
    {
        using var response = await SendAsync(method, path, "Receptionist");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(DoctorOnlyRoutes))]
    public async Task RouteWithAnAdminTokenReturns403(string method, string path)
    {
        using var response = await SendAsync(method, path, "Admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(DoctorOnlyRoutes))]
    public async Task RouteWithoutABearerTokenReturns401(string method, string path)
    {
        using var response = await SendAsync(method, path, role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Templates are saved and removed, never edited in place or deleted in bulk.
    [Theory]
    [InlineData("PUT", TemplatePath)]
    [InlineData("DELETE", TemplatesPath)]
    public async Task UnsupportedMethodHasNoGatewayRoute(string method, string path)
    {
        using var response = await SendAsync(method, path, "Doctor");

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
