namespace PatientService.Models.Configuration;

public sealed class ClinicOptions
{
    public const string SectionName = "Clinic";

    public string TimeZoneId { get; set; } = string.Empty;
}
