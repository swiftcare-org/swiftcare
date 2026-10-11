using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

public sealed class OutboxDeliveryTests : IDisposable
{
    private readonly SqliteConnection _connection = OpenConnection();
    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }
    public void Dispose() => _connection.Dispose();

    private QueueDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<QueueDbContext>().UseSqlite(_connection).AddInterceptors(interceptors).Options);

    private static OutboxMessage NewMessage(DateTime occurredAt) => OutboxMessage.Create(Guid.NewGuid(), new PatientCalledEvent
    {
        EventId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        QueueNumber = "Q-001",
        DoctorId = Guid.NewGuid(),
        DoctorName = "Doctor",
        RoomNumber = "R-1",
        CalledAt = occurredAt,
        CorrelationId = "persisted-correlation"
    }, occurredAt);

    private async Task<OutboxMessage> SeedAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var message = NewMessage(DateTime.UtcNow);
        // The persisted envelope and payload share the same event identity.
        var payload = JsonSerializer.Deserialize<PatientCalledEvent>(message.Payload)!;
        message.Id = payload.EventId;
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        return message;
    }

    [Fact]
    public async Task FailedDeliverySurvivesANewContextWithTheSamePayload()
    {
        var message = await SeedAsync();
        var failed = new Mock<IQueueEventPublisher>();
        failed.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await using (var context = CreateContext())
        {
            Assert.Equal(0, await OutboxDelivery.DeliverPendingAsync(context, failed.Object, NullLogger.Instance));
            Assert.Equal(message.Payload, (await context.OutboxMessages.SingleAsync()).Payload);
        }
        var recovered = new Mock<IQueueEventPublisher>();
        recovered.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCalledEvent, CancellationToken>((payload, _) => Assert.Equal(message.Payload, JsonSerializer.Serialize(payload)))
            .ReturnsAsync(true);
        await using var restarted = CreateContext();
        Assert.Equal(1, await OutboxDelivery.DeliverPendingAsync(restarted, recovered.Object, NullLogger.Instance));
        Assert.Empty(await restarted.OutboxMessages.ToListAsync());
        recovered.Verify(item => item.PublishPatientCalledAsync(It.Is<PatientCalledEvent>(payload => payload.EventId == message.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgementFailureRedeliversTheSameEvent()
    {
        var message = await SeedAsync();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await using (var context = CreateContext(new FailedAcknowledgement()))
        {
            Assert.False(await OutboxDelivery.TryDeliverAsync(context, publisher.Object,
                await context.OutboxMessages.SingleAsync(), NullLogger.Instance));
        }
        await using var restarted = CreateContext();
        Assert.Equal(message.Id, (await restarted.OutboxMessages.SingleAsync()).Id);
        Assert.Equal(1, await OutboxDelivery.DeliverPendingAsync(restarted, publisher.Object, NullLogger.Instance));
        Assert.Empty(await restarted.OutboxMessages.ToListAsync());
        publisher.Verify(item => item.PublishPatientCalledAsync(It.Is<PatientCalledEvent>(payload => payload.EventId == message.Id),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("invalid json")]
    public async Task InvalidPayloadRemainsPending(string payload)
    {
        var message = await SeedAsync();
        await using var context = CreateContext();
        var pending = await context.OutboxMessages.SingleAsync();
        pending.Payload = payload;
        await context.SaveChangesAsync();
        var publisher = new Mock<IQueueEventPublisher>(MockBehavior.Strict);
        Assert.False(await OutboxDelivery.TryDeliverAsync(context, publisher.Object, pending, NullLogger.Instance));
        Assert.Equal(message.Id, (await context.OutboxMessages.SingleAsync()).Id);
        publisher.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublisherExceptionsLeaveTheEventPending(bool cancellationException)
    {
        await SeedAsync();
        await using var context = CreateContext();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(cancellationException ? new OperationCanceledException() : new InvalidOperationException("Broker unavailable"));
        Assert.Equal(0, await OutboxDelivery.DeliverPendingAsync(context, publisher.Object, NullLogger.Instance));
        Assert.Single(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task RequestedCancellationPropagatesWithoutAcknowledgement()
    {
        await SeedAsync();
        await using var context = CreateContext();
        var message = await context.OutboxMessages.SingleAsync();
        using var cancellation = new CancellationTokenSource();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), cancellation.Token))
            .Callback(() => cancellation.Cancel()).ThrowsAsync(new OperationCanceledException(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OutboxDelivery.TryDeliverAsync(context, publisher.Object, message, NullLogger.Instance, cancellation.Token));
        Assert.Single(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task RelaySendsAtMostOneHundredInChronologicalOrder()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var messages = Enumerable.Range(0, 101).Select(index =>
        {
            var message = NewMessage(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index));
            return message;
        }).ToArray();
        context.OutboxMessages.AddRange(messages.Reverse());
        await context.SaveChangesAsync();
        var sent = new List<string>();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCalledEvent, CancellationToken>((payload, _) => sent.Add(JsonSerializer.Serialize(payload))).ReturnsAsync(true);
        Assert.Equal(100, await OutboxDelivery.DeliverPendingAsync(context, publisher.Object, NullLogger.Instance));
        Assert.Equal(messages.Take(100).Select(message => message.Payload), sent);
        Assert.Equal(messages[100].Id, (await context.OutboxMessages.SingleAsync()).Id);
    }

    [Fact]
    public async Task FailedFirstMessageStopsTheBatchWithoutLosingLaterMessages()
    {
        await SeedAsync();
        await using var context = CreateContext();
        context.OutboxMessages.Add(NewMessage(DateTime.UtcNow.AddMinutes(1)));
        await context.SaveChangesAsync();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.Equal(0, await OutboxDelivery.DeliverPendingAsync(context, publisher.Object, NullLogger.Instance));
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        publisher.Verify(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BackgroundRelayRetriesAfterAScopeFailureAndStopsCleanly()
    {
        await SeedAsync();
        var attempts = 0;
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var services = new ServiceCollection();
        services.AddScoped<QueueDbContext>(_ => Interlocked.Increment(ref attempts) == 1
            ? throw new InvalidOperationException("Temporary database outage") : CreateContext(new SavedAcknowledgement(sent)));
        services.AddSingleton(publisher.Object);
        await using var provider = services.BuildServiceProvider();
        using var relay = new OutboxRelay(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxRelay>.Instance);
        await relay.StartAsync(CancellationToken.None);
        try
        {
            await sent.Task.WaitAsync(TimeSpan.FromSeconds(18));
            await relay.StopAsync(CancellationToken.None);
            await using var context = CreateContext();
            Assert.Empty(await context.OutboxMessages.ToListAsync());
            Assert.Equal(2, attempts);
        }
        finally
        {
            await relay.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FailedAcknowledgement : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Acknowledgement failed");
    }

    private sealed class SavedAcknowledgement(TaskCompletionSource completed) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            completed.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
