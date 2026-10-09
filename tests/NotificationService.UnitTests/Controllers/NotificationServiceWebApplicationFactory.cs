using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NotificationService.Services;

namespace NotificationService.UnitTests.Controllers;

// Boots the real ASP.NET Core pipeline (startup checks, routing, model binding and the
// gateway secret middleware). The Kafka consumer is removed and the feed service is a
// mock, so no broker or database is needed.
public sealed class NotificationServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string ValidGatewaySecret = "integration-test-gateway-secret-value";

    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

    public NotificationServiceWebApplicationFactory()
        : this(new Dictionary<string, string?>())
    {
    }

    public NotificationServiceWebApplicationFactory(IReadOnlyDictionary<string, string?> configurationOverrides)
    {
        _configurationOverrides = configurationOverrides;
    }

    public Mock<INotificationFeedService> FeedServiceMock { get; } = new();

    public Mock<IDailyReportService> DailyReportServiceMock { get; } = new();

    public Mock<IMonthlyReportService> MonthlyReportServiceMock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:NotificationDb"] = "Server=localhost;Database=unused;User=unused;Password=unused;",
                ["Gateway:InternalSecret"] = ValidGatewaySecret,
                ["Kafka:BootstrapServers"] = "unused:9092"
            };

            foreach (var (key, value) in _configurationOverrides)
            {
                settings[key] = value;
            }

            configBuilder.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<INotificationFeedService>();
            services.AddScoped(_ => FeedServiceMock.Object);
            services.RemoveAll<IDailyReportService>();
            services.AddScoped(_ => DailyReportServiceMock.Object);
            services.RemoveAll<IMonthlyReportService>();
            services.AddScoped(_ => MonthlyReportServiceMock.Object);
        });
    }
}
