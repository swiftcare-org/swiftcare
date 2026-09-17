using MedicalRecordService.Models.Entities;

namespace MedicalRecordService.Data;

internal static class ConsultationTemplateSeedData
{
    // A fixed value keeps the migration and model snapshot deterministic.
    private static readonly DateTime SeedCreatedAt =
        new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<ConsultationTemplate> Templates { get; } =
    [
        new ConsultationTemplate
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Name = "General Consultation",
            Symptoms = "Presenting symptoms:\n- ",
            ExaminationFindings = "Examination findings:\n- ",
            Notes = "Assessment and plan:\n- ",
            IsActive = true,
            CreatedAt = SeedCreatedAt
        },
        new ConsultationTemplate
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            Name = "Respiratory Consultation",
            Symptoms = "Respiratory symptoms:\n- ",
            ExaminationFindings = "Respiratory examination findings:\n- ",
            Notes = "Respiratory assessment and plan:\n- ",
            IsActive = true,
            CreatedAt = SeedCreatedAt
        },
        new ConsultationTemplate
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
            Name = "Gastrointestinal Consultation",
            Symptoms = "Gastrointestinal symptoms:\n- ",
            ExaminationFindings = "Abdominal examination findings:\n- ",
            Notes = "Gastrointestinal assessment and plan:\n- ",
            IsActive = true,
            CreatedAt = SeedCreatedAt
        },
        new ConsultationTemplate
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
            Name = "Musculoskeletal Consultation",
            Symptoms = "Musculoskeletal symptoms:\n- ",
            ExaminationFindings = "Musculoskeletal examination findings:\n- ",
            Notes = "Musculoskeletal assessment and plan:\n- ",
            IsActive = true,
            CreatedAt = SeedCreatedAt
        }
    ];
}
