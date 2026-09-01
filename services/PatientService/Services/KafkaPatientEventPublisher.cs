using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using PatientService.Logging;
using PatientService.Models.Configuration;
using PatientService.Models.Events;

namespace PatientService.Services;

public sealed class KafkaPatientEventPublisher : IPatientEventPublisher
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaPatientEventPublisher> _logger;

    // The producer is injected rather than constructed here so tests can substitute a fake
    // IProducer<string, string> without touching a real broker. Program.cs registers the
    // real one as a singleton (thread-safe, expensive to construct - one per process).
    public KafkaPatientEventPublisher(
        IProducer<string, string> producer,
        IOptions<KafkaOptions> options,
        ILogger<KafkaPatientEventPublisher> logger)
    {
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> PublishPatientCheckedInAsync(
        Guid patientId,
        bool isNewPatient,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var checkedInEvent = new PatientCheckedInEvent
        {
            EventId = Guid.NewGuid(),
            PatientId = patientId,
            IsNewPatient = isNewPatient,
            CheckedInAt = DateTime.UtcNow,
            CorrelationId = correlationId
        };

        var message = new Message<string, string>
        {
            Key = patientId.ToString(),
            Value = JsonSerializer.Serialize(checkedInEvent),
            Headers = new Headers
            {
                { "X-Correlation-ID", Encoding.UTF8.GetBytes(correlationId) }
            }
        };

        // Bounds the wait to MessageTimeoutMs regardless of librdkafka's own internal
        // retry/timeout behavior - without this, an unreachable broker can leave the
        // registration request hanging well past what the caller expects.
        using var timeoutSource = new CancellationTokenSource(_options.MessageTimeoutMs);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        // Sanitized once and reused below: correlationId is client-supplied (it travels via
        // the X-Correlation-ID header), so every log statement that includes it must strip
        // CR/LF first to prevent a crafted header value from forging additional log lines.
        var sanitizedCorrelationId = LogSanitizer.Sanitize(correlationId);

        try
        {
            await _producer.ProduceAsync(_options.PatientCheckedInTopic, message, linkedSource.Token);
            return true;
        }
        catch (ProduceException<string, string> exception)
        {
            _logger.LogError(
                exception,
                "Failed to publish patient-checked-in event: patientId={PatientId} correlationId={CorrelationId}",
                patientId,
                sanitizedCorrelationId);
            return false;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            _logger.LogError(
                "Timed out publishing patient-checked-in event: patientId={PatientId} correlationId={CorrelationId}",
                patientId,
                sanitizedCorrelationId);
            return false;
        }
    }
}
