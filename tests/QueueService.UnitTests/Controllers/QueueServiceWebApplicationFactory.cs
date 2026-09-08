using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using QueueService.Services;

namespace QueueService.UnitTests.Controllers;

public sealed class QueueServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ValidGatewaySecret = "integration-test-gateway-secret-value";

    public Mock<IPatientQueueStatusService> PatientQueueStatusServiceMock { get; } = new();
    public Mock<ITodayQueueService> TodayQueueServiceMock { get; } = new();
    public Mock<ICallNextPatientService> CallNextPatientServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:QueueDb"] = "Server=localhost;Database=unused;User=unused;Password=unused;",
                ["Gateway:InternalSecret"] = ValidGatewaySecret,
                ["Kafka:BootstrapServers"] = "unused:9092",
                ["Kafka:PatientCheckedInTopic"] = "patient-checked-in",
                ["Kafka:PatientCalledTopic"] = "patient-called",
                ["Kafka:ConsumerGroupId"] = "queue-service-tests",
                ["Queue:ClinicTimeZone"] = "Asia/Colombo"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IPatientQueueStatusService>();
            services.RemoveAll<ITodayQueueService>();
            services.RemoveAll<ICallNextPatientService>();
            services.AddScoped(_ => PatientQueueStatusServiceMock.Object);
            services.AddScoped(_ => TodayQueueServiceMock.Object);
            services.AddScoped(_ => CallNextPatientServiceMock.Object);
        });
    }
}
