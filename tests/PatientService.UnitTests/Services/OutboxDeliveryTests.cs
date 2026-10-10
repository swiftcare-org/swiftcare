using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PatientService.Data;
using PatientService.Models.Entities;
using PatientService.Models.Events;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

public sealed class OutboxDeliveryTests
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    private PatientDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<PatientDbContext>().UseInMemoryDatabase(_databaseName).AddInterceptors(interceptors).Options);

    private static OutboxMessage NewMessage(DateTime occurredAt) => OutboxMessage.Create(Guid.NewGuid(), new PatientCheckedInEvent
    {
        EventId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        IsNewPatient = true,
        CheckedInAt = occurredAt,
        CorrelationId = "persisted-correlation"
    }, occurredAt);

    private async Task<OutboxMessage> SeedAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var message = NewMessage(DateTime.UtcNow);
        // The persisted envelope and payload share the same event identity.
        var payload = JsonSerializer.Deserialize<PatientCheckedInEvent>(message.Payload)!;
        message.Id = payload.EventId;
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        return message;
    }

    [Fact]
    public async Task FailedDeliverySurvivesANewContextWithTheSamePayload()
    {
        var message = await SeedAsync();
        var failed = new Mock<IPatientEventPublisher>();
        failed.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await using (var context = CreateContext())
        {
            Assert.Equal(0, await OutboxDelivery.DeliverPendingAsync(context, failed.Object, NullLogger.Instance));
            Assert.Equal(message.Payload, (await context.OutboxMessages.SingleAsync()).Payload);
        }
        var recovered = new Mock<IPatientEventPublisher>();
        recovered.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCheckedInEvent, CancellationToken>((payload, _) => Assert.Equal(message.Payload, JsonSerializer.Serialize(payload)))
            .ReturnsAsync(true);
        await using var restarted = CreateContext();
        Assert.Equal(1, await OutboxDelivery.DeliverPendingAsync(restarted, recovered.Object, NullLogger.Instance));
        Assert.Empty(await restarted.OutboxMessages.ToListAsync());
        recovered.Verify(item => item.PublishAsync(It.Is<PatientCheckedInEvent>(payload => payload.EventId == message.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgementFailureRedeliversTheSameEvent()
    {
        var message = await SeedAsync();
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await using (var context = CreateContext(new FailedAcknowledgement()))
        {
            Assert.False(await OutboxDelivery.TryDeliverAsync(context, publisher.Object,
                await context.OutboxMessages.SingleAsync(), NullLogger.Instance));
        }
        await using var restarted = CreateContext();
        Assert.Equal(message.Id, (await restarted.OutboxMessages.SingleAsync()).Id);
        Assert.Equal(1, await OutboxDelivery.DeliverPendingAsync(restarted, publisher.Object, NullLogger.Instance));
        Assert.Empty(await restarted.OutboxMessages.ToListAsync());
        publisher.Verify(item => item.PublishAsync(It.Is<PatientCheckedInEvent>(payload => payload.EventId == message.Id),
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
        var publisher = new Mock<IPatientEventPublisher>(MockBehavior.Strict);
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
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>()))
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
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), cancellation.Token))
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
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCheckedInEvent, CancellationToken>((payload, _) => sent.Add(JsonSerializer.Serialize(payload))).ReturnsAsync(true);
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
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.Equal(0, await OutboxDelivery.DeliverPendingAsync(context, publisher.Object, NullLogger.Instance));
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        publisher.Verify(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BackgroundRelayRetriesAfterAScopeFailureAndStopsCleanly()
    {
        await SeedAsync();
        var attempts = 0;
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var services = new ServiceCollection();
        services.AddScoped<PatientDbContext>(_ => Interlocked.Increment(ref attempts) == 1
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
