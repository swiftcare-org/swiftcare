namespace NotificationService.Models.Configuration;

public sealed class ReportOptions
{
    // A report covers one clinic day, which is not the same span as a UTC day.
    public string ClinicTimeZone { get; set; } = "Asia/Colombo";

    // Listed in every report, with 0 for a room that saw no patients.
    public IReadOnlyList<string> Rooms { get; set; } = ["1", "2", "3"];
}
