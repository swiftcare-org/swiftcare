namespace QueueService.Models.Configuration;

public sealed class QueueOptions
{
    // A UTC-date reset would roll the daily queue-number sequence over at 05:30 local time
    // in Sri Lanka instead of midnight, so QueueDate is always derived from this zone rather
    // than from the consumer's own UtcNow.
    public required string ClinicTimeZone { get; set; }

    public int MaxAllocationAttempts { get; set; } = 3;
}
