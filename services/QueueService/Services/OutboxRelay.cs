using QueueService.Data;

namespace QueueService.Services;

public sealed class OutboxRelay(IServiceScopeFactory scopes, ILogger<OutboxRelay> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await OutboxDelivery.DeliverPendingAsync(scope.ServiceProvider.GetRequiredService<QueueDbContext>(),
                        scope.ServiceProvider.GetRequiredService<IQueueEventPublisher>(), logger, stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Pending event scan failed and will be retried.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
