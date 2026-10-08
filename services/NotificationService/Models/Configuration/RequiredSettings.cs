namespace NotificationService.Models.Configuration;

// The settings the service cannot run without. Checked once at startup so a missing
// value stops the service before it serves a request, and the error names the setting.
public static class RequiredSettings
{
    public static readonly IReadOnlyList<string> Keys =
    [
        "ConnectionStrings:NotificationDb",
        "Gateway:InternalSecret",
        // Presence only, never reachability: the service must start and serve /health
        // even when Kafka is down. The consumer retries instead of blocking startup.
        "Kafka:BootstrapServers"
    ];

    public static void EnsurePresent(IConfiguration configuration)
    {
        var missing = Keys.FirstOrDefault(key => string.IsNullOrWhiteSpace(configuration[key]));
        if (missing is not null)
        {
            throw new InvalidOperationException(
                $"{missing} is not configured. Set it via the {missing.Replace(":", "__")} environment variable.");
        }
    }
}
