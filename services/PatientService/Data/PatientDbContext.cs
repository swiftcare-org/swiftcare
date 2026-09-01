using Microsoft.EntityFrameworkCore;
using PatientService.Models.Entities;

namespace PatientService.Data;

public sealed class PatientDbContext(DbContextOptions<PatientDbContext> options) : DbContext(options)
{
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Allergy> Allergies => Set<Allergy>();

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

        modelBuilder.Entity<Allergy>(entity =>
        {
            entity.Property(a => a.Id).ValueGeneratedNever();
            // Every read is "live allergies for one patient", so this composite index
            // serves it directly rather than filtering after a PatientId-only seek.
            entity.HasIndex(a => new { a.PatientId, a.IsDeleted });
            entity.Property(a => a.AllergyName).HasMaxLength(128).IsRequired();
            entity.Property(a => a.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(a => a.Notes).HasMaxLength(512);
            entity.HasOne<Patient>()
                .WithMany()
                .HasForeignKey(a => a.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
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
