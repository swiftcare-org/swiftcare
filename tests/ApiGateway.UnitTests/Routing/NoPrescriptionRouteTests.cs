using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.UnitTests.Routing;

// SWC-130: gateway authorization for recording that a consultation needs no prescription.
public class NoPrescriptionRouteTests
{
    private const string Path = "/api/consultations/4d8f6f0e-3b0f-4b5a-9d6e-7f1c2a9b8c11/no-prescription";

    private static async Task<HttpResponseMessage> PostAsync(string? role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", factory.CreateSignedToken(role: role));
        }

        return await client.PostAsync(Path, new StringContent("{}"));
    }

    // PrescriptionService is not running here, so a proxied request fails downstream. This
    // asserts only that the Gateway itself matched the route and let the doctor through.
    [Fact]
    public async Task RouteWithADoctorTokenPassesGatewayAuthorization()
    {
        using var response = await PostAsync("Doctor");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task RouteWithANonDoctorTokenReturns403(string role)
    {
        using var response = await PostAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RouteWithoutABearerTokenReturns401()
    {
        using var response = await PostAsync(role: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The path sits under /api/consultations, which otherwise belongs to MedicalRecordService.
    // The decision is stored beside prescriptions, so it must reach PrescriptionService.
    [Fact]
    public void RouteIsProxiedToThePrescriptionService()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        var route = configuration.GetSection("ReverseProxy:Routes:consultation-no-prescription-route");

        Assert.Equal("prescription-cluster", route["ClusterId"]);
        Assert.Equal("DoctorOnly", route["AuthorizationPolicy"]);
        var methods = route.GetSection("Match:Methods").Get<string[]>();
        Assert.NotNull(methods);
        Assert.Equal(["POST"], methods);
    }
}
