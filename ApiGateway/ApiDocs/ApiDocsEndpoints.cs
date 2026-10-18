using Scalar.AspNetCore;

namespace ApiGateway.ApiDocs;

// Public, read-only API documentation, switched on by ApiDocs:Enabled. When it is off nothing
// below is mapped, so /scalar and /openapi return 404. The pages only describe the API: every
// "try it" request goes to the public gateway (ApiDocs:PublicBaseUrl) and passes the same
// authentication and role policies as any other client call.
public static class ApiDocsEndpoints
{
    public const string HttpClientName = "api-docs";

    // Public document name and title. The name also identifies the reverse-proxy cluster
    // ("<name>-cluster" with destination "<name>-destination") that hosts the service.
    public static readonly IReadOnlyList<ApiDocsService> Services =
    [
        new("auth", "AuthService"),
        new("patient", "PatientService"),
        new("queue", "QueueService"),
        new("medical-record", "MedicalRecordService"),
        new("prescription", "PrescriptionService"),
        new("notification", "NotificationService")
    ];

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("ApiDocs:Enabled");

    public static void MapApiDocs(this WebApplication app)
    {
        // Each service's own OpenAPI document, fetched from the address the reverse proxy uses.
        app.MapGet("/openapi/{service}/v1.json", async (
            string service,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var target = Services.FirstOrDefault(s => s.Name == service);
            if (target is null)
            {
                return Results.NotFound();
            }

            // The reverse proxy refuses to start without these addresses, so they always exist here.
            var address = configuration[$"ReverseProxy:Clusters:{target.Cluster}:Destinations:{target.Destination}:Address"]!;

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(
                new Uri(new Uri(address.TrimEnd('/') + "/"), "openapi/v1.json"), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }

            var document = await response.Content.ReadAsStringAsync(cancellationToken);
            return Results.Content(document, "application/json");
        });

        // /scalar lists every service; /scalar/{name} opens one of them.
        var publicBaseUrl = app.Configuration["ApiDocs:PublicBaseUrl"];
        app.MapScalarApiReference("/scalar", options =>
        {
            options.WithTitle("SwiftCare API");
            options.WithOpenApiRoutePattern("/openapi/{documentName}/v1.json");
            foreach (var service in Services)
            {
                options.AddDocument(service.Name, service.Title);
            }

            // The documents list the services' internal addresses; send requests to the gateway.
            if (!string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                options.AddServer(publicBaseUrl.TrimEnd('/'));
            }
        });
    }
}

public sealed record ApiDocsService(string Name, string Title)
{
    public string Cluster => $"{Name}-cluster";

    public string Destination => $"{Name}-destination";
}
