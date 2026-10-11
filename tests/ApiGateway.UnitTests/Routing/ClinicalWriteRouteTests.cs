using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApiGateway.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiGateway.UnitTests.Routing;

public sealed class ClinicalWriteRouteTests
{
    [Theory]
    [InlineData("/api/consultations")]
    [InlineData("/api/prescriptions")]
    [InlineData("/api/consultations/11111111-1111-1111-1111-111111111111/no-prescription")]
    public async Task IneligibleVisitIsBlockedBeforeTheDownstreamSave(string path)
    {
        var validator = new RejectingValidator();
        using var factory = new ApiGatewayWebApplicationFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IClinicalVisitValidator>();
            services.AddSingleton<IClinicalVisitValidator>(validator);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken(role: "Doctor"));
        using var response = await client.PostAsync(path, new StringContent(JsonSerializer.Serialize(new
        {
            PatientId = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            ConsultationId = Guid.NewGuid()
        }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, validator.Calls);
    }

    [Fact]
    public async Task InternalVisitLookupHasNoPublicProxyRoute()
    {
        using var factory = new ApiGatewayWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateSignedToken(role: "Doctor"));
        using var response = await client.GetAsync($"/internal/visits/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class RejectingValidator : IClinicalVisitValidator
    {
        public int Calls { get; private set; }
        public Task<bool> ValidateAsync(Guid doctorId, Guid patientId, Guid queueId, Guid? consultationId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(false);
        }
    }
}
