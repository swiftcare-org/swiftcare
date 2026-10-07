using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrescriptionService.Data;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Services;

// SWC-130: how the existing prescription operations behave once a consultation has been
// recorded as needing no prescription.
public class PrescriptionManagementServiceNoPrescriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 4, 30, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task PrescriptionCannotBeSavedForAConsultationMarkedAsNeedingNone()
    {
        await using var scope = await TestScope.CreateAsync();
        var request = ValidRequest();
        await scope.SeedAsync(Decision(request.ConsultationId, request.QueueId));

        var result = await scope.Service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen");

        Assert.Equal(CreatePrescriptionOutcome.NoPrescriptionRequiredRecorded, result.Outcome);
        Assert.Null(result.Prescription);
        Assert.Empty(await scope.DbContext.Prescriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task DecisionForAnotherConsultationDoesNotBlockAPrescription()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.SeedAsync(Decision(Guid.NewGuid(), Guid.NewGuid()));

        var result = await scope.Service.CreateAsync(ValidRequest(), Guid.NewGuid(), "Dr. Amara Chen");

        Assert.Equal(CreatePrescriptionOutcome.Success, result.Outcome);
        Assert.Single(await scope.DbContext.Prescriptions.AsNoTracking().ToListAsync());
    }

    // The existing prescription wins the message: the doctor is told it already exists.
    [Fact]
    public async Task ExistingPrescriptionIsReportedBeforeTheNoPrescriptionCheck()
    {
        await using var scope = await TestScope.CreateAsync();
        var request = ValidRequest();
        await scope.Service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen");

        var second = await scope.Service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen");

        Assert.Equal(CreatePrescriptionOutcome.ConsultationAlreadyHasPrescription, second.Outcome);
    }

    private static CreatePrescriptionRequest ValidRequest() => new()
    {
        ConsultationId = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        Medicines =
        [
            new PrescriptionItemRequest
            {
                MedicineName = "Amoxicillin",
                Dosage = "500 mg",
                Frequency = "Twice daily",
                Duration = "5 days"
            }
        ]
    };

    private static NoPrescriptionDecision Decision(Guid consultationId, Guid queueId) => new()
    {
        Id = Guid.NewGuid(),
        ConsultationId = consultationId,
        QueueId = queueId,
        PatientId = Guid.NewGuid(),
        DoctorId = Guid.NewGuid(),
        DoctorName = "Dr. Amara Chen",
        RecordedAt = Now.UtcDateTime
    };

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestScope(SqliteConnection connection, PrescriptionDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = new PrescriptionManagementService(dbContext, new FixedTimeProvider());
        }

        public PrescriptionDbContext DbContext { get; }

        public PrescriptionManagementService Service { get; }

        public static async Task<TestScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var dbContext = new PrescriptionDbContext(
                new DbContextOptionsBuilder<PrescriptionDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await dbContext.Database.EnsureCreatedAsync();
            return new TestScope(connection, dbContext);
        }

        public async Task SeedAsync(params NoPrescriptionDecision[] decisions)
        {
            DbContext.NoPrescriptionDecisions.AddRange(decisions);
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
