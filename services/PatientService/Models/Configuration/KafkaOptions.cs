namespace PatientService.Models.Configuration;

public sealed class KafkaOptions
{
    public required string BootstrapServers { get; set; }
    public required string PatientCheckedInTopic { get; set; }

    // Bounds both librdkafka's own message.timeout.ms and the CancellationTokenSource
    // wrapped around ProduceAsync in KafkaPatientEventPublisher. Without this, an
    // unreachable broker leaves the HTTP request hanging for librdkafka's 300-second
    // default instead of failing fast.
    public int MessageTimeoutMs { get; set; } = 5000;
}
