namespace PrescriptionService.Models.Configuration;

public sealed class ClinicOptions
{
    public const string SectionName = "Clinic";

    public string TimeZone { get; set; } = string.Empty;
}
