using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGateway.UnitTests.Routing;

// Public API documentation at /scalar, /scalar/{service} and /openapi/{service}/v1.json exists
// only while ApiDocs:Enabled is true, and it never opens the API itself.
public class ApiDocsRouteTests
{
    private const string PublicBaseUrl = "https://api.swiftcare.test";

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent("{\"openapi\":\"3.1.1\",\"info\":{\"title\":\"stub\"}}", Encoding.UTF8, "application/json")
            });
        }
    }

    private static HttpClient CreateClient(ApiGatewayWebApplicationFactory factory, bool? enabled, StubHandler? handler = null)
    {
        var configured = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?> { ["ApiDocs:PublicBaseUrl"] = PublicBaseUrl };
                if (enabled is not null)
                {
                    values["ApiDocs:Enabled"] = enabled.Value ? "true" : "false";
                }

                config.AddInMemoryCollection(values);
            });
            if (handler is not null)
            {
                builder.ConfigureServices(services =>
                    services.AddHttpClient("api-docs").ConfigurePrimaryHttpMessageHandler(() => handler));
            }
        });
        var client = configured.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task DocsDoNotExistUnlessEnabled(bool? enabled)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = CreateClient(factory, enabled);

        foreach (var path in new[] { "/scalar", "/scalar/auth", "/openapi/auth/v1.json" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/scalar")]
    [InlineData("/scalar/auth")]
    [InlineData("/scalar/patient")]
    [InlineData("/scalar/queue")]
    [InlineData("/scalar/medical-record")]
    [InlineData("/scalar/prescription")]
    [InlineData("/scalar/notification")]
    public async Task EveryDocsPageIsPublicWhenEnabled(string path)
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = CreateClient(factory, enabled: true);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DocsPageSendsTryItRequestsToThePublicGateway()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = CreateClient(factory, enabled: true);

        var page = await client.GetStringAsync("/scalar/auth");

        Assert.Contains(PublicBaseUrl, page);
        Assert.Contains("openapi/auth/v1.json", page);
    }

    [Fact]
    public async Task ServiceDocumentIsFetchedFromTheServiceClusterAddress()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var handler = new StubHandler(HttpStatusCode.OK);
        using var client = CreateClient(factory, enabled: true, handler);

        using var response = await client.GetAsync("/openapi/patient/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync());
        var upstream = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:59999/openapi/v1.json", upstream.ToString());
    }

    [Fact]
    public async Task UnknownServiceDocumentIsNotFound()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        var handler = new StubHandler(HttpStatusCode.OK);
        using var client = CreateClient(factory, enabled: true, handler);

        using var response = await client.GetAsync("/openapi/billing/v1.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UpstreamFailureIsReportedAsBadGateway()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = CreateClient(factory, enabled: true, new StubHandler(HttpStatusCode.NotFound));

        using var response = await client.GetAsync("/openapi/auth/v1.json");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedApiStillRequiresATokenWhenDocsAreEnabled()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = CreateClient(factory, enabled: true);

        using var response = await client.GetAsync("/api/queue/today");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
