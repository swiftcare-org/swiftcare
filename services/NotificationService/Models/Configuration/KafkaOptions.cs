namespace NotificationService.Models.Configuration;

public sealed class KafkaOptions
{
    public required string BootstrapServers { get; set; }
    public string PatientCheckedInTopic { get; set; } = "patient-checked-in";
    public string PatientCalledTopic { get; set; } = "patient-called";
    public string ConsultationCompletedTopic { get; set; } = "consultation-completed";

    // Its own group, so this service receives every event regardless of what the other
    // consumers of the same topics have already read.
    public string ConsumerGroupId { get; set; } = "notification-service";

    // How long the consumer waits after a failed attempt to store an event before it reads
    // the same event again. Configurable so tests can keep it short.
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> Topics =>
        [PatientCheckedInTopic, PatientCalledTopic, ConsultationCompletedTopic];
}
