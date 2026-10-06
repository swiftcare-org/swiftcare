using AuthService.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<LoginAuditEntry> LoginAuditEntries => Set<LoginAuditEntry>();
    public DbSet<LogoutAuditEntry> LogoutAuditEntries => Set<LogoutAuditEntry>();
    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Id).ValueGeneratedNever();
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(u => u.FullName).HasMaxLength(128).IsRequired();
            entity.Property(u => u.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(u => u.RoomNumber).HasMaxLength(16);
            entity.Property(u => u.Specialization).HasMaxLength(64);
        });

        modelBuilder.Entity<LoginAuditEntry>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.OccurredAt);
            entity.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
        });

        modelBuilder.Entity<LogoutAuditEntry>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.OccurredAt);
            entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
        });

        modelBuilder.Entity<AdminAuditEntry>(entity =>
        {
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.HasIndex(e => e.OccurredAt);
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
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
