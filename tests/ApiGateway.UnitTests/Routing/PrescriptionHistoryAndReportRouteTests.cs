using System.Net;
using System.Net.Http.Headers;

namespace ApiGateway.UnitTests.Routing;

// SWC-142 and SWC-145: gateway authorization for the prescription history, daily report
// and monthly report routes.
public class PrescriptionHistoryAndReportRouteTests
{
    private const string HistoryPath = "/api/prescriptions/patient/4d8f6f0e-3b0f-4b5a-9d6e-7f1c2a9b8c11";
    private const string DailyReportPath = "/api/prescriptions/report/daily?date=2026-10-02";
    private const string MonthlyReportPath = "/api/prescriptions/report/monthly?month=2026-10";

    [Theory]
    [InlineData(HistoryPath, "Doctor")]
    [InlineData(DailyReportPath, "Admin")]
    [InlineData(MonthlyReportPath, "Admin")]
    public async Task RouteWithThePermittedRolePassesGatewayAuthorization(string path, string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(HistoryPath, "Receptionist")]
    [InlineData(HistoryPath, "Admin")]
    [InlineData(DailyReportPath, "Doctor")]
    [InlineData(DailyReportPath, "Receptionist")]
    [InlineData(MonthlyReportPath, "Doctor")]
    [InlineData(MonthlyReportPath, "Receptionist")]
    public async Task RouteWithAnotherRoleReturns403(string path, string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(HistoryPath)]
    [InlineData(DailyReportPath)]
    [InlineData(MonthlyReportPath)]
    public async Task RouteWithoutBearerTokenReturns401(string path)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
