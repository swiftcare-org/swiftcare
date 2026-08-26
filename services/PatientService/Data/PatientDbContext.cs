using Microsoft.EntityFrameworkCore;
using PatientService.Models.Entities;

namespace PatientService.Data;

public sealed class PatientDbContext(DbContextOptions<PatientDbContext> options) : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.HasIndex(p => p.Nic).IsUnique();
            entity.Property(p => p.Nic).HasMaxLength(12).IsRequired();
            entity.Property(p => p.FullName).HasMaxLength(128).IsRequired();
            entity.Property(p => p.Gender).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(p => p.Address).HasMaxLength(256).IsRequired();
            entity.Property(p => p.PhoneNumber).HasMaxLength(16).IsRequired();
            entity.Property(p => p.BloodGroup).HasConversion<string>().HasMaxLength(16).IsRequired();
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
