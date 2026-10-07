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

    [Fact]
    public async Task QueueEntryMarkedAsNeedingNoPrescriptionIsReportedAsNotRequiredWithDoctorAndTime()
    {
        await using var scope = await TestScope.CreateAsync();
        var decision = Decision(Guid.NewGuid(), Guid.NewGuid());
        await scope.SeedAsync(decision);

        var result = await scope.Service.GetByQueueIdAsync(decision.QueueId);

        Assert.NotNull(result);
        Assert.Equal("NOT_REQUIRED", result.Status);
        Assert.Equal(decision.Id, result.Id);
        Assert.Equal(decision.ConsultationId, result.ConsultationId);
        Assert.Equal(decision.QueueId, result.QueueId);
        Assert.Equal(decision.PatientId, result.PatientId);
        Assert.Equal(decision.DoctorId, result.DoctorId);
        Assert.Equal("Dr. Amara Chen", result.DoctorName);
        Assert.Equal(Now.UtcDateTime, result.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, result.CreatedAt.Kind);
        Assert.Empty(result.Medicines);
    }

    [Fact]
    public async Task QueueEntryWithAPrescriptionStillReturnsThePrescription()
    {
        await using var scope = await TestScope.CreateAsync();
        var request = ValidRequest();
        await scope.Service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen");
        await scope.SeedAsync(Decision(Guid.NewGuid(), Guid.NewGuid()));

        var result = await scope.Service.GetByQueueIdAsync(request.QueueId);

        Assert.NotNull(result);
        Assert.Equal(Prescription.PendingStatus, result.Status);
        Assert.Equal("Amoxicillin", Assert.Single(result.Medicines).MedicineName);
    }

    [Fact]
    public async Task QueueEntryWithNeitherAPrescriptionNorADecisionReturnsNothing()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.SeedAsync(Decision(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Null(await scope.Service.GetByQueueIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DecisionIsLeftOutOfThePendingPrescriptionsList()
    {
        await using var scope = await TestScope.CreateAsync();
        var request = ValidRequest();
        await scope.Service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen");
        await scope.SeedAsync(Decision(Guid.NewGuid(), Guid.NewGuid()));

        var pending = await scope.Service.GetPendingAsync();

        Assert.Equal(request.ConsultationId, Assert.Single(pending).ConsultationId);
    }

    [Fact]
    public async Task DecisionIsLeftOutOfThePatientPrescriptionHistory()
    {
        await using var scope = await TestScope.CreateAsync();
        var decision = Decision(Guid.NewGuid(), Guid.NewGuid());
        await scope.SeedAsync(decision);

        Assert.Empty(await scope.Service.GetForPatientAsync(decision.PatientId));
    }

    // A decision is not a prescription, so there is nothing to dispense or to add medicine to.
    [Fact]
    public async Task DecisionCannotBeDispensedOrGivenMedicines()
    {
        await using var scope = await TestScope.CreateAsync();
        var decision = Decision(Guid.NewGuid(), Guid.NewGuid());
        await scope.SeedAsync(decision);

        var dispense = await scope.Service.DispenseAsync(decision.Id, "Front Desk");
        var addMedicine = await scope.Service.AddMedicineAsync(
            decision.Id, ValidRequest().Medicines[0], decision.DoctorId);

        Assert.Equal(DispensePrescriptionOutcome.PrescriptionNotFound, dispense.Outcome);
        Assert.Equal(PrescriptionItemChangeOutcome.PrescriptionNotFound, addMedicine.Outcome);
    }
}
