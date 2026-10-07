using Confluent.Kafka;
using Microsoft.Extensions.Options;
using NotificationService.Logging;
using NotificationService.Models.Configuration;

namespace NotificationService.Services;

// Reads the three department topics in one loop and stores each event as a notification.
// An event is committed only after it is stored, so a failure never loses one.
public sealed class ActivityEventConsumer : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationEventParser _parser;
    private readonly KafkaOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ActivityEventConsumer> _logger;

    public ActivityEventConsumer(
        IConsumer<string, string> consumer,
        IServiceScopeFactory scopeFactory,
        IOptions<KafkaOptions> options,
        TimeProvider timeProvider,
        ILogger<ActivityEventConsumer> logger)
    {
        _consumer = consumer;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _parser = new NotificationEventParser(_options);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _consumer.Subscribe(_options.Topics);
        _logger.LogInformation(
            "Subscribed to activity topics as {GroupId}",
            _options.ConsumerGroupId);

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
                    // The Kafka error code and reason describe the broker failure and hold no payload.
                    _logger.LogError(
                        "Kafka consume error on activity topics: kafkaErrorCode={KafkaErrorCode} kafkaErrorReason={KafkaErrorReason}",
                        exception.Error.Code,
                        LogSanitizer.Sanitize(exception.Error.Reason));
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
        var notification = _parser.Parse(
            result.Topic,
            result.Message.Value,
            _timeProvider.GetUtcNow().UtcDateTime);

        if (notification is null)
        {
            // Retrying cannot fix a malformed event, so it is skipped. The payload is not
            // logged: it is not known to be safe.
            _logger.LogError(
                "Invalid activity event; skipping message: topic={Topic} position={Offset}",
                LogSanitizer.Sanitize(result.Topic),
                result.TopicPartitionOffset.Offset.Value);
            CommitSafely(result);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recorder = scope.ServiceProvider.GetRequiredService<INotificationRecorder>();
            var outcome = await recorder.RecordAsync(notification, stoppingToken);
            _logger.LogInformation(
                "Activity event handled: eventId={EventId} type={Type} outcome={Outcome}",
                notification.EventId,
                notification.Type,
                outcome);
            CommitSafely(result);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Leave the offset uncommitted and read the same event again. Only the exception
            // type is logged: its message could carry data from the failing statement.
            _logger.LogError(
                "Storing activity event failed; retrying eventId={EventId} errorType={ErrorType}",
                notification.EventId,
                exception.GetType().Name);
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
        catch (KafkaException exception)
        {
            // Kafka may redeliver; the unique event ID makes that safe.
            _logger.LogError(
                "Failed to commit activity event offset: position={Offset} errorType={ErrorType} kafkaErrorCode={KafkaErrorCode} kafkaErrorReason={KafkaErrorReason}",
                result.TopicPartitionOffset.Offset.Value,
                exception.GetType().Name,
                exception.Error.Code,
                LogSanitizer.Sanitize(exception.Error.Reason));
        }
    }

    public override void Dispose()
    {
        _consumer.Dispose();
        base.Dispose();
    }
}
