using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QueueService.Data;
using QueueService.Models.Entities;
using QueueService.Models.Events;
using QueueService.Services;

namespace QueueService.UnitTests.Services;

// The tie-break of the delivery order (SWC-151 mutation testing).
public sealed class OutboxDeliveryEdgeCaseTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    // Events created at the same moment are delivered in the same order on every attempt.
    [Fact]
    public async Task EventsCreatedAtTheSameTimeAreDeliveredInIdOrder()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var context = new QueueDbContext(new DbContextOptionsBuilder<QueueDbContext>()
            .UseSqlite(connection)
            .Options);
        await context.Database.EnsureCreatedAsync();
        context.OutboxMessages.AddRange(
            Message("00000000-0000-0000-0000-000000000002", "second"),
            Message("00000000-0000-0000-0000-000000000001", "first"));
        await context.SaveChangesAsync();
        var sent = new List<string>();
        var publisher = new Mock<IQueueEventPublisher>();
        publisher.Setup(item => item.PublishPatientCalledAsync(It.IsAny<PatientCalledEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCalledEvent, CancellationToken>((payload, _) => sent.Add(payload.CorrelationId))
            .ReturnsAsync(true);

        Assert.Equal(2, await OutboxDelivery.DeliverPendingAsync(context, publisher.Object, NullLogger.Instance));
        Assert.Equal(["first", "second"], sent);
    }

    private static OutboxMessage Message(string id, string correlationId)
    {
        var message = OutboxMessage.Create(Guid.NewGuid(), new PatientCalledEvent
        {
            EventId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            QueueNumber = "Q-001",
            DoctorId = Guid.NewGuid(),
            DoctorName = "Doctor",
            RoomNumber = "R-1",
            CalledAt = CreatedAt,
            CorrelationId = correlationId
        }, CreatedAt);
        message.Id = Guid.Parse(id);
        return message;
    }
}
