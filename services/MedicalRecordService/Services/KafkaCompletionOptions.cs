namespace MedicalRecordService.Services;

public sealed class KafkaCompletionOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string ConsultationCompletedTopic { get; set; } = "consultation-completed";
    public int MessageTimeoutMs { get; set; } = 5000;
}
