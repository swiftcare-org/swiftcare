using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ApiGateway.Models;

namespace ApiGateway.UnitTests.Routing;

// Exercises the real appsettings.json ReverseProxy route configuration end to end through
// the ASP.NET Core pipeline, closing the gap left by the pure middleware-unit tests: those
// prove the middleware logic is correct in isolation, but not that the "anonymous"/"default"
// AuthorizationPolicy strings and route Order values in appsettings.json actually produce
// the intended behavior when wired together.
public class ReverseProxyAuthorizationTests
{
    [Fact]
    public async Task LoginRouteDoesNotRequireAuthentication()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        var response = await client.PostAsync("/api/auth/login", new StringContent("{}"));

        // AuthService isn't running in this environment, so a successful proxy attempt
        // fails downstream (e.g. 502) rather than succeeding - what this asserts is only
        // that the Gateway itself did not block the request for lacking a bearer token.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LogoutRouteWithoutABearerTokenIsRejectedWith401BeforeReachingAuthService()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
    }

    [Fact]
    public async Task LogoutRouteWithAValidBearerTokenPassesGatewayAuthorization()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken());

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRouteWithARevokedTokenIsRejectedWith401()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var jti = Guid.NewGuid().ToString();
        factory.RevokedTokenStore.Revoke(jti, DateTimeOffset.UtcNow.AddHours(1));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken(jti: jti));

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedRouteWithAnExpiredTokenIsRejectedWith401WithAMessageBody()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            factory.CreateSignedToken(expiresAtUtc: DateTime.UtcNow.AddHours(-1)));

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
    }

    [Fact]
    public async Task HealthCheckDoesNotRequireAuthentication()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UsersRouteWithoutABearerTokenIsRejectedWith401()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    public async Task UsersRouteWithANonAdminTokenIsRejectedWith403(string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
    }

    [Fact]
    public async Task UsersRouteWithATokenCarryingNoRoleClaimIsRejectedWith403()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken());

        var response = await client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UsersRouteWithAnAdminTokenPassesGatewayAuthorization()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: "Admin"));

        var response = await client.GetAsync("/api/users");

        // AuthService isn't running in this environment, so a successful proxy attempt
        // fails downstream rather than succeeding - what this asserts is only that the
        // Gateway itself did not block an admin token for lacking the required role.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatientsRouteWithoutABearerTokenIsRejectedWith401()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/patients", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Admin")]
    public async Task PatientsRouteWithANonReceptionistTokenIsRejectedWith403(string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var response = await client.PostAsync("/api/patients", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
    }

    [Fact]
    public async Task PatientsRouteWithAReceptionistTokenPassesGatewayAuthorization()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: "Receptionist"));

        var response = await client.PostAsync("/api/patients", new StringContent("{}"));

        // PatientService isn't running in this environment, so a successful proxy attempt
        // fails downstream rather than succeeding - what this asserts is only that the
        // Gateway itself did not block a receptionist token for lacking the required role.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatientsSearchRouteWithoutABearerTokenIsRejectedWith401()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/patients/search?q=Test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Unauthorized", body!.Message);
    }

    // SWC-17 widened /api/patients/search from Receptionist-only to Doctor, Receptionist,
    // and Admin, since search is the only navigation path to a patient's profile and
    // allergy alert.
    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task PatientsSearchRouteWithAnAuthorizedRoleTokenPassesGatewayAuthorization(string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var response = await client.GetAsync("/api/patients/search?q=Test");

        // PatientService isn't running in this environment, so a successful proxy attempt
        // fails downstream rather than succeeding - what this asserts is only that the
        // Gateway itself did not block this role for lacking the required role.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatientsSearchRouteWithAnUnauthorizedRoleTokenIsRejectedWith403()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: "Nurse"));

        var response = await client.GetAsync("/api/patients/search?q=Test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.Equal("Forbidden", body!.Message);
    }

    // Proves route precedence structurally rather than by Order alone: patient-read-route
    // is constrained to {id:guid}, so "search" cannot bind to it regardless of Order. If
    // patients-search-route were ever removed or misconfigured, this request would 404 at
    // the Gateway (no matching route) instead of silently falling through to the read
    // route - which is exactly the regression this guards against.
    [Fact]
    public async Task PatientSearchRouteIsNotShadowedByThePatientReadRoute()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: "Receptionist"));

        var response = await client.GetAsync("/api/patients/search?q=Test");

        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatientReadRouteWithANonGuidIdSegmentReturns404AtTheGateway()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: "Doctor"));

        var response = await client.GetAsync("/api/patients/not-a-guid");

        // No route matches a non-guid id segment - proves {id:guid} is actually enforced
        // at the Gateway, not just documented in appsettings.json.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task PatientReadRouteIsReachableByAllThreeRoles(string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var response = await client.GetAsync($"/api/patients/{Guid.NewGuid()}");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatientReadRouteWithAnUnauthorizedRoleTokenIsRejectedWith403()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: "Nurse"));

        var response = await client.GetAsync($"/api/patients/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    [InlineData("Admin")]
    public async Task AllergyReadRouteIsReachableByAllThreeRoles(string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var response = await client.GetAsync($"/api/patients/{Guid.NewGuid()}/allergies");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Receptionist")]
    public async Task AllergyWriteRoutesAreReachableByDoctorAndReceptionist(string role)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: role));

        var postResponse = await client.PostAsync($"/api/patients/{Guid.NewGuid()}/allergies", new StringContent("{}"));
        Assert.NotEqual(HttpStatusCode.Unauthorized, postResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var putResponse = await client.PutAsync(
            $"/api/patients/{Guid.NewGuid()}/allergies/{Guid.NewGuid()}", new StringContent("{}"));
        Assert.NotEqual(HttpStatusCode.Unauthorized, putResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, putResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/patients/{Guid.NewGuid()}/allergies/{Guid.NewGuid()}");
        Assert.NotEqual(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task AllergyWriteRoutesRejectAdminWith403()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", factory.CreateSignedToken(role: "Admin"));

        var postResponse = await client.PostAsync($"/api/patients/{Guid.NewGuid()}/allergies", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var putResponse = await client.PutAsync(
            $"/api/patients/{Guid.NewGuid()}/allergies/{Guid.NewGuid()}", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/patients/{Guid.NewGuid()}/allergies/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task AllergyRoutesWithoutABearerTokenAreRejectedWith401()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();

        var getResponse = await client.GetAsync($"/api/patients/{Guid.NewGuid()}/allergies");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        var postResponse = await client.PostAsync($"/api/patients/{Guid.NewGuid()}/allergies", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    [Fact]
    public async Task ConfiguredFrontendOriginReceivesCorsHeader()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", ApiGatewayWebApplicationFactory.TestFrontendOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            ApiGatewayWebApplicationFactory.TestFrontendOrigin,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }
}
