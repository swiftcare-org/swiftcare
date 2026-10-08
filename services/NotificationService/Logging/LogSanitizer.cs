namespace NotificationService.Logging;

// A value that comes from outside the service, such as the request path or a Kafka error
// reason, can contain CR/LF sequences crafted to forge additional, fake log lines.
// Every log statement that includes such a string must sanitize it first.
public static class LogSanitizer
{
    public static string Sanitize(string? value) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
}
