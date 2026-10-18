using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace PatientService.UnitTests.Controllers;

// Outside Development, the OpenAPI document exists only while ApiDocs:Enabled is true, so the
// gateway can publish it; by default production serves nothing at /openapi.
public class OpenApiDocumentTests
{
    private static async Task<HttpResponseMessage> GetDocumentAsync(string? enabled)
    {
        using var factory = new PatientServiceWebApplicationFactory();
        using var configured = factory.WithWebHostBuilder(builder =>
        {
            if (enabled is not null)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?> { ["ApiDocs:Enabled"] = enabled }));
            }
        });
        using var client = configured.CreateClient();
        return await client.GetAsync("/openapi/v1.json");
    }

    [Fact]
    public async Task DocumentIsPublishedWhenApiDocsIsEnabled()
    {
        using var response = await GetDocumentAsync("true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task DocumentDoesNotExistUnlessApiDocsIsEnabled(string? enabled)
    {
        using var response = await GetDocumentAsync(enabled);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
