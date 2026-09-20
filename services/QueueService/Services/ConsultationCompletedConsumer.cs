using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using QueueService.Models.Configuration;
using QueueService.Models.Events;

namespace QueueService.Services;

public sealed class ConsultationCompletedConsumer : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _options;
    private readonly ILogger<ConsultationCompletedConsumer> _logger;

    public ConsultationCompletedConsumer(
        IConsumer<string, string> consumer,
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        ILogger<ConsultationCompletedConsumer> logger)
    {
        _consumer = consumer;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _consumer.Subscribe(_options.ConsultationCompletedTopic);
        _logger.LogInformation(
            "Subscribed to consultation-completed topic {Topic} as {GroupId}",
            _options.ConsultationCompletedTopic,
            _options.ConsultationCompletedConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = _consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException exception)
                {
                    _logger.LogError(
                        "Kafka consume error on consultation-completed: {Reason}",
                        exception.Error.Reason);
                    continue;
                }

                if (result?.Message?.Value is not null)
                {
                    await ProcessMessageAsync(result, stoppingToken);
                }
            }
        }
        finally
        {
            _consumer.Close();
        }
    }

    private async Task ProcessMessageAsync(
        ConsumeResult<string, string> result,
        CancellationToken stoppingToken)
    {
        ConsultationCompletedEvent? completedEvent;
        try
        {
            completedEvent = JsonSerializer.Deserialize<ConsultationCompletedEvent>(result.Message.Value);
        }
        catch (JsonException)
        {
            _logger.LogError("Invalid consultation-completed JSON; skipping message");
            CommitSafely(result);
            return;
        }

        if (completedEvent is null
            || completedEvent.EventId == Guid.Empty
            || completedEvent.ConsultationId == Guid.Empty
            || completedEvent.QueueId == Guid.Empty
            || completedEvent.PatientId == Guid.Empty
            || completedEvent.DoctorId == Guid.Empty)
        {
            _logger.LogError("Invalid consultation-completed identifiers; skipping message");
            CommitSafely(result);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var completionService = scope.ServiceProvider.GetRequiredService<IQueueCompletionService>();
            var outcome = await completionService.CompleteAsync(completedEvent, stoppingToken);
            _logger.LogInformation(
                "Processed consultation-completed event: eventId={EventId} outcome={Outcome}",
                completedEvent.EventId,
                outcome);
            CommitSafely(result);
        }
        catch (Exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Leave the offset uncommitted and retry the same event. The transaction
            // ensures a failed queue update does not record the event ID as processed.
            _logger.LogError(
                "Consultation-completed processing failed; retrying eventId={EventId}",
                completedEvent.EventId);
            _consumer.Seek(result.TopicPartitionOffset);
            await Task.Delay(_options.RetryDelay, stoppingToken);
        }
    }

    private void CommitSafely(ConsumeResult<string, string> result)
    {
        try
        {
            _consumer.Commit(result);
        }
        catch (KafkaException)
        {
            // Kafka may redeliver; the ProcessedEvents ledger makes that safe.
            _logger.LogError(
                "Failed to commit consultation-completed offset: event position={Offset}",
                result.TopicPartitionOffset);
        }
    }

    public override void Dispose()
    {
        _consumer.Dispose();
        base.Dispose();
    }
}
