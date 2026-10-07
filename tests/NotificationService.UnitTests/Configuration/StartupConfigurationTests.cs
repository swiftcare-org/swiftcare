using NotificationService.Models.Configuration;
using NotificationService.UnitTests.Controllers;

namespace NotificationService.UnitTests.Configuration;

// The service refuses to start half-configured, and names the setting that is missing.
public class StartupConfigurationTests
{
    [Theory]
    [InlineData("ConnectionStrings:NotificationDb", "Connection string 'ConnectionStrings:NotificationDb' is not configured.")]
    [InlineData("Gateway:InternalSecret", "Gateway:InternalSecret is not configured.")]
    [InlineData("Kafka:BootstrapServers", "Kafka:BootstrapServers is not configured.")]
    public void StartupFailsFastWhenARequiredSettingIsMissing(string setting, string expectedMessage)
    {
        using var factory = new NotificationServiceWebApplicationFactory(
            new Dictionary<string, string?> { [setting] = string.Empty });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.StartsWith(expectedMessage, exception.Message);
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
