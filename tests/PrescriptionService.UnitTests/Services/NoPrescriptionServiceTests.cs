using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PrescriptionService.Data;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Services;

// SWC-130: a doctor records that a completed consultation needs no prescription.
public class NoPrescriptionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 4, 30, 0, TimeSpan.Zero);
    private static readonly Guid ConsultationId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task RecordStoresTheDecisionWithTheDoctorAndTheTime()
    {
        await using var scope = await TestScope.CreateAsync();
        var request = ValidRequest();

        var result = await scope.Service.RecordAsync(ConsultationId, request, DoctorId, "  Dr. Amara Chen  ");

        Assert.Equal(RecordNoPrescriptionOutcome.Recorded, result.Outcome);
        var stored = await scope.DbContext.NoPrescriptionDecisions.AsNoTracking().SingleAsync();
        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal(ConsultationId, stored.ConsultationId);
        Assert.Equal(request.QueueId, stored.QueueId);
        Assert.Equal(request.PatientId, stored.PatientId);
        Assert.Equal(DoctorId, stored.DoctorId);
        Assert.Equal("Dr. Amara Chen", stored.DoctorName);
        Assert.Equal(Now.UtcDateTime, stored.RecordedAt);
    }

    [Fact]
    public async Task RecordReturnsTheDecisionAsNotRequiredWithNoMedicines()
    {
        await using var scope = await TestScope.CreateAsync();
        var request = ValidRequest();

        var result = await scope.Service.RecordAsync(ConsultationId, request, DoctorId, "Dr. Amara Chen");

        var decision = result.Decision;
        Assert.NotNull(decision);
        var stored = await scope.DbContext.NoPrescriptionDecisions.AsNoTracking().SingleAsync();
        Assert.Equal(stored.Id, decision.Id);
        Assert.Equal(ConsultationId, decision.ConsultationId);
        Assert.Equal(request.QueueId, decision.QueueId);
        Assert.Equal(request.PatientId, decision.PatientId);
        Assert.Equal(DoctorId, decision.DoctorId);
        Assert.Equal("Dr. Amara Chen", decision.DoctorName);
        Assert.Equal("NOT_REQUIRED", decision.Status);
        Assert.Equal(Now.UtcDateTime, decision.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, decision.CreatedAt.Kind);
        Assert.Empty(decision.Medicines);
        Assert.Null(decision.DispensedBy);
        Assert.Null(decision.DispensedAt);
    }

    [Fact]
    public async Task RecordCreatesNoPrescription()
    {
        await using var scope = await TestScope.CreateAsync();

        await scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, "Dr. Amara Chen");

        Assert.Empty(await scope.DbContext.Prescriptions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConsultationThatAlreadyHasAPrescriptionCannotBeMarked()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.DbContext.Prescriptions.Add(NewPrescription(ConsultationId));
        await scope.DbContext.SaveChangesAsync();

        var result = await scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, "Dr. Amara Chen");

        Assert.Equal(RecordNoPrescriptionOutcome.ConsultationAlreadyHasPrescription, result.Outcome);
        Assert.Null(result.Decision);
        Assert.Empty(await scope.DbContext.NoPrescriptionDecisions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task PrescriptionForAnotherConsultationDoesNotBlockTheDecision()
    {
        await using var scope = await TestScope.CreateAsync();
        scope.DbContext.Prescriptions.Add(NewPrescription(Guid.NewGuid()));
        await scope.DbContext.SaveChangesAsync();

        var result = await scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, "Dr. Amara Chen");

        Assert.Equal(RecordNoPrescriptionOutcome.Recorded, result.Outcome);
    }

    [Fact]
    public async Task SecondDecisionForTheSameConsultationIsRejectedAndTheFirstIsKept()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, "Dr. Amara Chen");

        var second = await scope.Service.RecordAsync(ConsultationId, ValidRequest(), Guid.NewGuid(), "Dr. Other");

        Assert.Equal(RecordNoPrescriptionOutcome.AlreadyRecorded, second.Outcome);
        Assert.Null(second.Decision);
        var stored = await scope.DbContext.NoPrescriptionDecisions.AsNoTracking().SingleAsync();
        Assert.Equal("Dr. Amara Chen", stored.DoctorName);
    }

    [Fact]
    public async Task DecisionForAnotherConsultationDoesNotBlockThisOne()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.Service.RecordAsync(Guid.NewGuid(), ValidRequest(), DoctorId, "Dr. Amara Chen");

        var result = await scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, "Dr. Amara Chen");

        Assert.Equal(RecordNoPrescriptionOutcome.Recorded, result.Outcome);
        Assert.Equal(2, await scope.DbContext.NoPrescriptionDecisions.CountAsync());
    }

    [Fact]
    public async Task RecordReturnsAlreadyRecordedWhenAConcurrentRequestSavedTheSameConsultationFirst()
    {
        await using var scope = await TestScope.CreateAsync(async connection =>
        {
            // A second request for the same consultation commits between this request's
            // check and its save, so the unique index rejects the save.
            await using var other = await CreateDbContextAsync(connection);
            other.NoPrescriptionDecisions.Add(new NoPrescriptionDecision
            {
                Id = Guid.NewGuid(),
                ConsultationId = ConsultationId,
                QueueId = Guid.NewGuid(),
                PatientId = Guid.NewGuid(),
                DoctorId = Guid.NewGuid(),
                DoctorName = "Dr. First",
                RecordedAt = Now.UtcDateTime
            });
            await other.SaveChangesAsync();
        });

        var result = await scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, "Dr. Amara Chen");

        Assert.Equal(RecordNoPrescriptionOutcome.AlreadyRecorded, result.Outcome);
        Assert.Null(result.Decision);
    }

    [Fact]
    public async Task RecordRethrowsASaveFailureThatIsNotAConflict()
    {
        await using var scope = await TestScope.CreateAsync(_ =>
            throw new DbUpdateException("Simulated database failure"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, "Dr. Amara Chen"));

        Assert.Equal("Simulated database failure", exception.Message);
    }

    [Fact]
    public async Task RecordRejectsAMissingRequest()
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            scope.Service.RecordAsync(ConsultationId, null!, DoctorId, "Dr. Amara Chen"));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task RecordRejectsAnEmptyConsultationId()
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.RecordAsync(Guid.Empty, ValidRequest(), DoctorId, "Dr. Amara Chen"));

        Assert.Equal("consultationId", exception.ParamName);
        Assert.StartsWith("Consultation ID must be provided.", exception.Message);
        Assert.Empty(await scope.DbContext.NoPrescriptionDecisions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RecordRejectsAnEmptyDoctorId()
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.RecordAsync(ConsultationId, ValidRequest(), Guid.Empty, "Dr. Amara Chen"));

        Assert.Equal("doctorId", exception.ParamName);
        Assert.StartsWith("Doctor ID must be provided.", exception.Message);
        Assert.Empty(await scope.DbContext.NoPrescriptionDecisions.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RecordRejectsABlankDoctorName(string doctorName)
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.RecordAsync(ConsultationId, ValidRequest(), DoctorId, doctorName));

        Assert.Equal("doctorName", exception.ParamName);
        Assert.Empty(await scope.DbContext.NoPrescriptionDecisions.AsNoTracking().ToListAsync());
    }

    private static RecordNoPrescriptionRequest ValidRequest() => new()
    {
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid()
    };

    private static Prescription NewPrescription(Guid consultationId) => new()
    {
        Id = Guid.NewGuid(),
        ConsultationId = consultationId,
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        DoctorId = Guid.NewGuid(),
        DoctorName = "Dr. Amara Chen",
        Status = Prescription.PendingStatus,
        CreatedAt = Now.UtcDateTime,
        UpdatedAt = Now.UtcDateTime
    };

    private static async Task<PrescriptionDbContext> CreateDbContextAsync(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var dbContext = new PrescriptionDbContext(
            new DbContextOptionsBuilder<PrescriptionDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptors)
                .Options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    // Runs an action just before the service's own SaveChanges, to simulate what another
    // request or the database does at that moment.
    private sealed class BeforeSaveInterceptor(Func<SqliteConnection, Task> beforeSave)
        : SaveChangesInterceptor
    {
        public SqliteConnection? Connection { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await beforeSave(Connection!);
            return result;
        }
    }

    private sealed class TestScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestScope(SqliteConnection connection, PrescriptionDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = new NoPrescriptionService(dbContext, new FixedTimeProvider());
        }

        public PrescriptionDbContext DbContext { get; }

        public NoPrescriptionService Service { get; }

        public static async Task<TestScope> CreateAsync(
            Func<SqliteConnection, Task>? beforeServiceSave = null)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var interceptors = new List<IInterceptor>();
            if (beforeServiceSave is not null)
            {
                interceptors.Add(new BeforeSaveInterceptor(beforeServiceSave) { Connection = connection });
            }

            var dbContext = await CreateDbContextAsync(connection, [.. interceptors]);
            return new TestScope(connection, dbContext);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
