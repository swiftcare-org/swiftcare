using MedicalRecordService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;

namespace MedicalRecordService.UnitTests.Controllers;

public sealed class MedicalRecordServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ValidGatewaySecret = "integration-test-gateway-secret-value";

    public Mock<IConsultationService> ConsultationServiceMock { get; } = new();
    public Mock<IConsultationTemplateService> TemplateServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MedicalRecordDb"] =
                    "Server=localhost;Database=unused;User=unused;Password=unused;",
                ["Gateway:InternalSecret"] = ValidGatewaySecret
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IConsultationService>();
            services.RemoveAll<IConsultationTemplateService>();
            services.AddScoped(_ => ConsultationServiceMock.Object);
            services.AddScoped(_ => TemplateServiceMock.Object);
        });
    }
}
