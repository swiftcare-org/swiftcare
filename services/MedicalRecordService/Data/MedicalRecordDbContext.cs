using MedicalRecordService.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MedicalRecordService.Data;

public sealed class MedicalRecordDbContext(DbContextOptions<MedicalRecordDbContext> options)
    : DbContext(options)
{
    public DbSet<Consultation> Consultations => Set<Consultation>();
    public DbSet<ConsultationTemplate> ConsultationTemplates => Set<ConsultationTemplate>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The medical-record schema deliberately has no standalone TemplateId index.
        // Suppress EF's foreign-key index convention so migrations remain deterministic.
        configurationBuilder.Conventions.Remove(typeof(ForeignKeyIndexConvention));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasCharSet("utf8mb4");

        modelBuilder.Entity<ConsultationTemplate>(entity =>
        {
            entity.ToTable("ConsultationTemplates");
            entity.HasKey(template => template.Id);

            entity.Property(template => template.Id)
                .HasColumnType("char(36)")
                .HasCharSet("ascii")
                .UseCollation("ascii_bin")
                .ValueGeneratedNever();
            entity.Property(template => template.Name)
                .HasColumnType("varchar(150)")
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(template => template.Symptoms)
                .HasColumnType("text")
                .IsRequired();
            entity.Property(template => template.ExaminationFindings)
                .HasColumnType("text")
                .IsRequired();
            entity.Property(template => template.Notes)
                .HasColumnType("text")
                .IsRequired();
            entity.Property(template => template.IsActive)
                .HasColumnType("tinyint(1)")
                .HasDefaultValue(true)
                .IsRequired();
            entity.Property(template => template.CreatedAt)
                .HasColumnType("datetime(6)")
                .IsRequired();

            entity.HasIndex(template => template.Name)
                .IsUnique()
                .HasDatabaseName("UX_ConsultationTemplates_Name");

            entity.HasData(ConsultationTemplateSeedData.Templates);
        });

        modelBuilder.Entity<Consultation>(entity =>
        {
            entity.ToTable("Consultations");
            entity.HasKey(consultation => consultation.Id);

            ConfigureGuid(entity.Property(consultation => consultation.Id));
            ConfigureGuid(entity.Property(consultation => consultation.PatientId));
            ConfigureGuid(entity.Property(consultation => consultation.QueueId));
            ConfigureGuid(entity.Property(consultation => consultation.DoctorId));
            ConfigureOptionalGuid(entity.Property(consultation => consultation.TemplateId));

            entity.Property(consultation => consultation.Id).ValueGeneratedNever();
            entity.Property(consultation => consultation.DoctorName)
                .HasColumnType("varchar(200)")
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(consultation => consultation.RoomNumber)
                .HasColumnType("varchar(50)")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(consultation => consultation.Symptoms)
                .HasColumnType("text")
                .IsRequired();
            entity.Property(consultation => consultation.ExaminationFindings)
                .HasColumnType("text");
            entity.Property(consultation => consultation.Diagnosis)
                .HasColumnType("text")
                .IsRequired();
            entity.Property(consultation => consultation.Notes)
                .HasColumnType("text");
            entity.Property(consultation => consultation.TemplateName)
                .HasColumnType("varchar(150)")
                .HasMaxLength(150);
            entity.Property(consultation => consultation.ConsultationDate)
                .HasColumnType("datetime(6)")
                .IsRequired();
            entity.Property(consultation => consultation.CreatedAt)
                .HasColumnType("datetime(6)")
                .IsRequired();

            entity.HasIndex(consultation => consultation.QueueId)
                .IsUnique()
                .HasDatabaseName("UX_Consultations_QueueId");
            entity.HasIndex(consultation => consultation.PatientId)
                .HasDatabaseName("IX_Consultations_PatientId");
            entity.HasIndex(consultation => consultation.DoctorId)
                .HasDatabaseName("IX_Consultations_DoctorId");
            entity.HasIndex(consultation => consultation.ConsultationDate)
                .HasDatabaseName("IX_Consultations_ConsultationDate");

            entity.HasOne<ConsultationTemplate>()
                .WithMany()
                .HasForeignKey(consultation => consultation.TemplateId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Consultations_ConsultationTemplates_TemplateId");
        });
    }

    private static void ConfigureGuid(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<Guid> property)
    {
        property
            .HasColumnType("char(36)")
            .HasCharSet("ascii")
            .UseCollation("ascii_bin")
            .IsRequired();
    }

    private static void ConfigureOptionalGuid(
        Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<Guid?> property)
    {
        property
            .HasColumnType("char(36)")
            .HasCharSet("ascii")
            .UseCollation("ascii_bin");
    }
}
