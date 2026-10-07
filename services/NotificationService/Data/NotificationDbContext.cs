using Microsoft.EntityFrameworkCore;
using NotificationService.Models.Entities;

namespace NotificationService.Data;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options)
    : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(notification => notification.Id).ValueGeneratedNever();
            entity.Property(notification => notification.Type)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(notification => notification.QueueNumber).HasMaxLength(16);
            entity.Property(notification => notification.DoctorName).HasMaxLength(200);
            entity.Property(notification => notification.RoomNumber).HasMaxLength(50);

            // The final backstop against storing a redelivered event twice.
            entity.HasIndex(notification => notification.EventId).IsUnique();

            // The feed and the reports both read by time, newest first.
            entity.HasIndex(notification => notification.OccurredAt);
        });
    }
}
