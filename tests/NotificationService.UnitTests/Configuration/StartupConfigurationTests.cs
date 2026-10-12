using NotificationService.Models.Configuration;
using NotificationService.UnitTests.Controllers;

namespace NotificationService.UnitTests.Configuration;

// The service refuses to start half-configured, and names the setting that is missing.
public class StartupConfigurationTests
{
    [Theory]
    [InlineData("ConnectionStrings:NotificationDb", "ConnectionStrings__NotificationDb")]
    [InlineData("Gateway:InternalSecret", "Gateway__InternalSecret")]
    [InlineData("Kafka:BootstrapServers", "Kafka__BootstrapServers")]
    public void StartupFailsFastWhenARequiredSettingIsMissing(string setting, string environmentVariable)
    {
        using var factory = new NotificationServiceWebApplicationFactory(
            new Dictionary<string, string?> { [setting] = string.Empty });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Equal(
            $"{setting} is not configured. Set it via the {environmentVariable} environment variable.",
            exception.Message);
    }

    [Fact]
    public void KafkaDefaultsMatchTheTopicsTheOtherServicesPublish()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };

        Assert.Equal("patient-checked-in", options.PatientCheckedInTopic);
        Assert.Equal("patient-called", options.PatientCalledTopic);
        Assert.Equal("consultation-completed", options.ConsultationCompletedTopic);
        Assert.Equal(
            ["patient-checked-in", "patient-called", "consultation-completed"],
            options.Topics);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RetryDelay);
    }

    // Sharing a group with QueueService would split the events between the two services.
    [Fact]
    public void ConsumerGroupIsTheServicesOwn()
    {
        var options = new KafkaOptions { BootstrapServers = "localhost:9092" };

        Assert.Equal("notification-service", options.ConsumerGroupId);
    }
}
