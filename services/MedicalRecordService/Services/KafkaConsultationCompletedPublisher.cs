using System.Text.Json;
using Confluent.Kafka;
using MedicalRecordService.Logging;
using MedicalRecordService.Models.Events;
using Microsoft.Extensions.Options;

namespace MedicalRecordService.Services;

public sealed class KafkaConsultationCompletedPublisher : IConsultationCompletedPublisher
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaCompletionOptions _options;
    private readonly ILogger<KafkaConsultationCompletedPublisher> _logger;

    public KafkaConsultationCompletedPublisher(
        IProducer<string, string> producer,
        IOptions<KafkaCompletionOptions> options,
        ILogger<KafkaConsultationCompletedPublisher> logger)
    {
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> PublishAsync(
        ConsultationCompletedEvent completedEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completedEvent);

        var message = new Message<string, string>
        {
            Key = completedEvent.QueueId.ToString(),
            Value = JsonSerializer.Serialize(completedEvent)
        };

        using var timeoutSource = new CancellationTokenSource(_options.MessageTimeoutMs);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);

        try
        {
            await _producer.ProduceAsync(
                _options.ConsultationCompletedTopic, message, linkedSource.Token);
            return true;
        }
        catch (ProduceException<string, string> exception)
        {
            // The Kafka error code and reason describe the broker failure and hold no
            // payload. The exception itself is not logged: it carries the message.
            _logger.LogError(
                "Failed to publish consultation-completed: eventId={EventId} errorType={ErrorType} kafkaErrorCode={KafkaErrorCode} kafkaErrorReason={KafkaErrorReason}",
                completedEvent.EventId,
                exception.GetType().Name,
                exception.Error.Code,
                LogSanitizer.Sanitize(exception.Error.Reason));
            return false;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            _logger.LogError(
                "Timed out publishing consultation-completed: eventId={EventId}",
                completedEvent.EventId);
            return false;
        }
    }
}
