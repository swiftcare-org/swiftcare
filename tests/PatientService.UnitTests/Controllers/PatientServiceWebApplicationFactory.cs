using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using PatientService.Data;
using PatientService.Services;

namespace PatientService.UnitTests.Controllers;

// Boots the real ASP.NET Core pipeline (routing, model validation, GatewaySecretMiddleware)
// so controller tests exercise the actual request path instead of calling the action in
// isolation. The database is swapped for EF Core InMemory, IPatientRegistrationService is
// swapped for a mock (its own behavior is covered by PatientRegistrationServiceTests), and
// IPatientEventPublisher is swapped for a mock so no real Kafka producer is constructed.
public sealed class PatientServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ValidGatewaySecret = "integration-test-gateway-secret-value";

    public Mock<IPatientRegistrationService> PatientRegistrationServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PatientDb"] = "Server=localhost;Database=unused;User=unused;Password=unused;",
                ["Gateway:InternalSecret"] = ValidGatewaySecret,
                ["Kafka:BootstrapServers"] = "unused:9092",
                ["Kafka:PatientCheckedInTopic"] = "patient-checked-in"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<PatientDbContext>>();
            services.AddDbContext<PatientDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

            services.RemoveAll<IPatientRegistrationService>();
            services.AddScoped(_ => PatientRegistrationServiceMock.Object);

            services.RemoveAll<Confluent.Kafka.IProducer<string, string>>();
            services.AddSingleton(_ => new Mock<Confluent.Kafka.IProducer<string, string>>().Object);
        });
    }
}
