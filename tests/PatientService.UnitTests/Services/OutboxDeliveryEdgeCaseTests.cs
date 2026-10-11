using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PatientService.Data;
using PatientService.Models.Entities;
using PatientService.Models.Events;
using PatientService.Services;

namespace PatientService.UnitTests.Services;

// The tie-break of the delivery order (SWC-151 mutation testing).
public sealed class OutboxDeliveryEdgeCaseTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    // Events created at the same moment are delivered in the same order on every attempt.
    [Fact]
    public async Task EventsCreatedAtTheSameTimeAreDeliveredInIdOrder()
    {
        await using var context = new PatientDbContext(new DbContextOptionsBuilder<PatientDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        context.OutboxMessages.AddRange(
            Message("00000000-0000-0000-0000-000000000002", "second"),
            Message("00000000-0000-0000-0000-000000000001", "first"));
        await context.SaveChangesAsync();
        var sent = new List<string>();
        var publisher = new Mock<IPatientEventPublisher>();
        publisher.Setup(item => item.PublishAsync(It.IsAny<PatientCheckedInEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PatientCheckedInEvent, CancellationToken>((payload, _) => sent.Add(payload.CorrelationId))
            .ReturnsAsync(true);

        Assert.Equal(2, await OutboxDelivery.DeliverPendingAsync(context, publisher.Object, NullLogger.Instance));
        Assert.Equal(["first", "second"], sent);
    }

    private static OutboxMessage Message(string id, string correlationId)
    {
        var message = OutboxMessage.Create(Guid.NewGuid(), new PatientCheckedInEvent
        {
            EventId = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            IsNewPatient = true,
            CheckedInAt = CreatedAt,
            CorrelationId = correlationId
        }, CreatedAt);
        message.Id = Guid.Parse(id);
        return message;
    }
}
