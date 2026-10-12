namespace QueueService.Models.Configuration;

public sealed class KafkaOptions
{
    public required string BootstrapServers { get; set; }
    public required string PatientCheckedInTopic { get; set; }
    public required string PatientCalledTopic { get; set; }
    public required string ConsumerGroupId { get; set; }
    public string ConsultationCompletedTopic { get; set; } = "consultation-completed";
    public string ConsultationCompletedConsumerGroup { get; set; } = "queue-service-consultations";

    public int MessageTimeoutMs { get; set; } = 5000;

    // How long PatientCheckedInConsumer waits after a transient processing failure before
    // re-consuming the same (Seek-rewound) message. Configurable rather than hardcoded so
    // tests can drive it down from the production default and stay fast.
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
}
