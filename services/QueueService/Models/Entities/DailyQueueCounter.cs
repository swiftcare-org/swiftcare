namespace QueueService.Models.Entities;

// One row per clinic-local day, allocating the next sequential queue number. LastNumber is
// configured as an EF Core concurrency token in QueueDbContext, so two concurrent consumer
// instances allocating for the same day race on an optimistic compare-and-swap rather than
// silently handing out the same number.
public sealed class DailyQueueCounter
{
    public DateOnly QueueDate { get; set; }
    public int LastNumber { get; set; }
}
