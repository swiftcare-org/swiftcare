namespace PatientService.Logging;

// A client can set values such as the request path or the X-Correlation-ID header to any
// string, including embedded CR/LF sequences crafted to forge additional, fake log lines.
// Every log statement that includes a client-supplied string must sanitize it first.
public static class LogSanitizer
{
    public static string Sanitize(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
