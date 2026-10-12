using System.Net;
using System.Net.Http.Headers;

namespace ApiGateway.UnitTests.Routing;

public class CompletedConsultationsRouteTests
{
    [Theory]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    [InlineData("Receptionist", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    public async Task CompletedVisitsAreRestrictedToDoctors(string? role, HttpStatusCode expected)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken(role: role));
        using var response = await client.GetAsync("/api/consultations/completed?page=0");
        Assert.Equal(expected, response.StatusCode);
    }
}
