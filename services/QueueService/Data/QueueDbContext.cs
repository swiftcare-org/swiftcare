using Microsoft.EntityFrameworkCore;
using QueueService.Models.Entities;

namespace QueueService.Data;

public sealed class QueueDbContext(DbContextOptions<QueueDbContext> options) : DbContext(options)
{
    public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
    public DbSet<DailyQueueCounter> DailyQueueCounters => Set<DailyQueueCounter>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<QueueEntry>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.QueueNumber).HasMaxLength(8).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(e => e.RoomNumber).HasMaxLength(16);

            // Scenario 4: a patient already checked in today cannot get a second entry.
            entity.HasIndex(e => new { e.PatientId, e.QueueDate }).IsUnique();

            // Final backstop against the counter ever handing out the same number twice for
            // one day, even under a bug in the allocation retry logic.
            entity.HasIndex(e => new { e.QueueDate, e.QueueNumber }).IsUnique();
        });

        modelBuilder.Entity<DailyQueueCounter>(entity =>
        {
            entity.HasKey(c => c.QueueDate);

            // Optimistic concurrency token: the allocation service updates this row with
            // `WHERE QueueDate = @d AND LastNumber = @old`, so a concurrent allocator racing
            // for the same day fails with DbUpdateConcurrencyException and retries instead of
            // silently overwriting the other allocator's increment.
            entity.Property(c => c.LastNumber).IsConcurrencyToken();
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(p => p.EventId);
            entity.Property(p => p.EventId).ValueGeneratedNever();
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IHasTimestamps>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
