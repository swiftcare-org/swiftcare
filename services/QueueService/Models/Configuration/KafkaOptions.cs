namespace QueueService.Models.Configuration;

public sealed class KafkaOptions
{
    public required string BootstrapServers { get; set; }
    public required string PatientCheckedInTopic { get; set; }
    public required string ConsumerGroupId { get; set; }
}
