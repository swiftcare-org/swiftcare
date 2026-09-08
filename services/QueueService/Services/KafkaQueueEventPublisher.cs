using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using QueueService.Logging;
using QueueService.Models.Configuration;
using QueueService.Models.Events;

namespace QueueService.Services;

public sealed class KafkaQueueEventPublisher : IQueueEventPublisher
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaQueueEventPublisher> _logger;

    public KafkaQueueEventPublisher(
        IProducer<string, string> producer,
        IOptions<KafkaOptions> options,
        ILogger<KafkaQueueEventPublisher> logger)
    {
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> PublishPatientCalledAsync(
        PatientCalledEvent patientCalledEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patientCalledEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(patientCalledEvent.CorrelationId);

        var message = new Message<string, string>
        {
            Key = patientCalledEvent.QueueId.ToString(),
            Value = JsonSerializer.Serialize(patientCalledEvent),
            Headers = new Headers
            {
                { "X-Correlation-ID", Encoding.UTF8.GetBytes(patientCalledEvent.CorrelationId) }
            }
        };

        using var timeoutSource = new CancellationTokenSource(_options.MessageTimeoutMs);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var sanitizedCorrelationId = LogSanitizer.Sanitize(patientCalledEvent.CorrelationId);

        try
        {
            await _producer.ProduceAsync(
                _options.PatientCalledTopic,
                message,
                linkedSource.Token);
            return true;
        }
        catch (ProduceException<string, string> exception)
        {
            _logger.LogError(
                exception,
                "Failed to publish patient-called event: queueId={QueueId} correlationId={CorrelationId}",
                patientCalledEvent.QueueId,
                sanitizedCorrelationId);
            return false;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            _logger.LogError(
                "Timed out publishing patient-called event: queueId={QueueId} correlationId={CorrelationId}",
                patientCalledEvent.QueueId,
                sanitizedCorrelationId);
            return false;
        }
    }
}
