using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using QueueService.Logging;
using QueueService.Models.Configuration;
using QueueService.Models.Events;

namespace QueueService.Services;

public sealed class PatientCheckedInConsumer : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<PatientCheckedInConsumer> _logger;

    public PatientCheckedInConsumer(
        IConsumer<string, string> consumer,
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<PatientCheckedInConsumer> logger)
    {
        _consumer = consumer;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Lets the host finish starting other hosted services before this loop begins,
        // matching the standard BackgroundService pattern.
        await Task.Yield();

        _consumer.Subscribe(_options.PatientCheckedInTopic);
        _logger.LogInformation(
            "Subscribed to Kafka topic {Topic} as consumer group {GroupId}",
            _options.PatientCheckedInTopic,
            _options.ConsumerGroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumeResult;

                try
                {
                    consumeResult = _consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume error on topic {Topic}", _options.PatientCheckedInTopic);
                    continue;
                }

                if (consumeResult?.Message?.Value is null)
                {
                    continue;
                }

                await ProcessMessageAsync(consumeResult, stoppingToken);
            }
        }
        finally
        {
            // Committed offsets and a clean group leave, so a redeployed instance resumes
            // from where this one stopped rather than the group's rebalance timeout kicking in.
            _consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(ConsumeResult<string, string> consumeResult, CancellationToken stoppingToken)
    {
        var correlationId = ExtractCorrelationId(consumeResult.Message.Headers);

        PatientCheckedInEvent? checkedInEvent;
        try
        {
            checkedInEvent = JsonSerializer.Deserialize<PatientCheckedInEvent>(consumeResult.Message.Value);
        }
        catch (JsonException ex)
        {
            // A poison message can never succeed no matter how many times it is redelivered,
            // so it is committed past (skipped) rather than retried forever.
            _logger.LogError(
                ex,
                "Failed to deserialize patient-checked-in event, skipping: correlationId={CorrelationId}",
                correlationId);
            CommitSafely(consumeResult);
            return;
        }

        if (checkedInEvent is null || checkedInEvent.EventId == Guid.Empty)
        {
            _logger.LogError(
                "Received an invalid patient-checked-in event payload, skipping: correlationId={CorrelationId}",
                correlationId);
            CommitSafely(consumeResult);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var creationService = scope.ServiceProvider.GetRequiredService<IQueueEntryCreationService>();

            await creationService.CreateQueueEntryAsync(
                checkedInEvent.EventId,
                checkedInEvent.PatientId,
                checkedInEvent.CheckedInAt,
                stoppingToken);

            CommitSafely(consumeResult);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transient failure (e.g. the database was unreachable) leaves the offset
            // uncommitted so Kafka redelivers this message. Seek rewinds this consumer's
            // local position so the very next Consume() returns the same message again
            // instead of silently skipping ahead to the next one.
            _logger.LogError(
                ex,
                "Failed to process patient-checked-in event, will retry: eventId={EventId} patientId={PatientId} correlationId={CorrelationId}",
                checkedInEvent.EventId,
                checkedInEvent.PatientId,
                correlationId);
            _consumer.Seek(consumeResult.TopicPartitionOffset);
            await Task.Delay(_options.RetryDelay, stoppingToken);
        }
    }

    private void CommitSafely(ConsumeResult<string, string> consumeResult)
    {
        try
        {
            _consumer.Commit(consumeResult);
        }
        catch (KafkaException ex)
        {
            _logger.LogError(
                ex,
                "Failed to commit Kafka offset: topic={Topic} partition={Partition} offset={Offset}",
                consumeResult.Topic,
                consumeResult.Partition.Value,
                consumeResult.Offset.Value);
        }
    }

    // X-Correlation-ID is client-supplied (it started as an HTTP header on the check-in
    // request before PatientService copied it onto this Kafka message), so it must be
    // sanitized before it ever reaches a log statement, the same as every other consumer
    // of this header across the codebase.
    private static string ExtractCorrelationId(Headers headers) =>
        headers.TryGetLastBytes("X-Correlation-ID", out var bytes)
            ? LogSanitizer.Sanitize(Encoding.UTF8.GetString(bytes))
            : string.Empty;
}
