namespace PrescriptionService.Logging;

public static class LogSanitizer
{
    public static string Sanitize(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
