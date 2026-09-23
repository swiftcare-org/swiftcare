using Microsoft.EntityFrameworkCore;
using PrescriptionService.Models.Entities;

namespace PrescriptionService.Data;

public sealed class PrescriptionDbContext(DbContextOptions<PrescriptionDbContext> options)
    : DbContext(options)
{
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Prescription>(entity =>
        {
            entity.Property(prescription => prescription.Id).ValueGeneratedNever();
            entity.Property(prescription => prescription.DoctorName).HasMaxLength(200).IsRequired();
            entity.Property(prescription => prescription.Status)
                .HasMaxLength(16)
                .HasDefaultValue(Prescription.PendingStatus)
                .IsRequired();

            // A consultation can produce only one prescription. This unique index is the
            // final backstop if two matching requests arrive concurrently.
            entity.HasIndex(prescription => prescription.ConsultationId).IsUnique();

            // Supports the patient-history query without copying any patient demographics.
            entity.HasIndex(prescription => new { prescription.PatientId, prescription.CreatedAt });

            entity.HasMany(prescription => prescription.Items)
                .WithOne(item => item.Prescription)
                .HasForeignKey(item => item.PrescriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrescriptionItem>(entity =>
        {
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.MedicineName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Dosage).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Frequency).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Duration).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Instructions).HasMaxLength(500);

            // Medicine rows are displayed in the order entered by the doctor.
            entity.HasIndex(item => new { item.PrescriptionId, item.ItemOrder }).IsUnique();
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Prescription>())
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
