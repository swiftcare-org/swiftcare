namespace MedicalRecordService.Models.Configuration;

public sealed class MedicalRecordOptions
{
    public const string SectionName = "Clinic";

    public string TimeZone { get; set; } = string.Empty;
}
