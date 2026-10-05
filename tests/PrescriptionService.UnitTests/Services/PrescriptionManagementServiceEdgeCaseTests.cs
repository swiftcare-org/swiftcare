using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PrescriptionService.Data;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Services;

// Guard clauses, tie-break ordering and failure paths of PrescriptionManagementService
// that the original suite did not reach (SWC-151 mutation testing).
public class PrescriptionManagementServiceEdgeCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid LowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid HigherId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task CreateRejectsNullRequest()
    {
        await using var scope = await TestScope.CreateAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            scope.Service.CreateAsync(null!, Guid.NewGuid(), "Dr. Amara Chen"));
    }

    [Fact]
    public async Task CreateRejectsEmptyDoctorIdBeforePersistence()
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.CreateAsync(ValidRequest(), Guid.Empty, "Dr. Amara Chen"));

        Assert.Equal("doctorId", exception.ParamName);
        Assert.StartsWith("Doctor ID must be provided.", exception.Message);
        Assert.Equal(0, await scope.DbContext.Prescriptions.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateRejectsBlankDoctorNameBeforePersistence(string doctorName)
    {
        await using var scope = await TestScope.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.CreateAsync(ValidRequest(), Guid.NewGuid(), doctorName));

        Assert.Equal(0, await scope.DbContext.Prescriptions.CountAsync());
    }

    [Fact]
    public async Task CreateReturnsConflictWhenAConcurrentRequestSavedTheSameConsultationFirst()
    {
        var request = ValidRequest();
        await using var scope = await TestScope.CreateAsync(async connection =>
        {
            // A second request for the same consultation commits between this request's
            // duplicate check and its save, so the unique index rejects the save.
            await using var other = await CreateDbContextAsync(connection);
            other.Prescriptions.Add(NewPrescription(Guid.NewGuid(), request.ConsultationId));
            await other.SaveChangesAsync();
        });

        var result = await scope.Service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen");

        Assert.Equal(CreatePrescriptionOutcome.ConsultationAlreadyHasPrescription, result.Outcome);
        Assert.Null(result.Prescription);
    }

    [Fact]
    public async Task CreateRethrowsSaveFailureThatIsNotADuplicate()
    {
        await using var scope = await TestScope.CreateAsync(_ =>
            throw new DbUpdateException("Simulated database failure"));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            scope.Service.CreateAsync(ValidRequest(), Guid.NewGuid(), "Dr. Amara Chen"));

        Assert.Equal("Simulated database failure", exception.Message);
    }

    [Fact]
    public async Task GetForPatientRejectsEmptyPatientId()
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.GetForPatientAsync(Guid.Empty));

        Assert.Equal("patientId", exception.ParamName);
        Assert.StartsWith("Patient ID must be provided.", exception.Message);
    }

    [Fact]
    public async Task GetForPatientOrdersPrescriptionsWrittenAtTheSameTimeByIdDescending()
    {
        var patientId = Guid.NewGuid();
        await using var scope = await TestScope.CreateAsync();
        await scope.SeedAsync(
            NewPrescription(LowerId, patientId: patientId),
            NewPrescription(HigherId, patientId: patientId));

        var history = await scope.Service.GetForPatientAsync(patientId);

        Assert.Equal([HigherId, LowerId], history.Select(prescription => prescription.Id));
    }

    [Fact]
    public async Task GetPendingOrdersPrescriptionsWrittenAtTheSameTimeByIdAscending()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.SeedAsync(NewPrescription(HigherId), NewPrescription(LowerId));

        var pending = await scope.Service.GetPendingAsync();

        Assert.Equal([LowerId, HigherId], pending.Select(prescription => prescription.Id));
    }

    [Fact]
    public async Task GetByQueueIdRejectsEmptyQueueId()
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.GetByQueueIdAsync(Guid.Empty));

        Assert.Equal("queueId", exception.ParamName);
        Assert.StartsWith("Queue ID must be provided.", exception.Message);
    }

    [Fact]
    public async Task GetByQueueIdReturnsNullForUnknownQueue()
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.SeedAsync(NewPrescription(LowerId));

        Assert.Null(await scope.Service.GetByQueueIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DispenseRejectsEmptyPrescriptionId()
    {
        await using var scope = await TestScope.CreateAsync();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.DispenseAsync(Guid.Empty, "Nimali Perera"));

        Assert.Equal("prescriptionId", exception.ParamName);
        Assert.StartsWith("Prescription ID must be provided.", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DispenseRejectsBlankReceptionistAndLeavesPrescriptionPending(string receptionistName)
    {
        await using var scope = await TestScope.CreateAsync();
        await scope.SeedAsync(NewPrescription(LowerId));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.DispenseAsync(LowerId, receptionistName));

        var stored = await scope.DbContext.Prescriptions.AsNoTracking().SingleAsync();
        Assert.Equal(Prescription.PendingStatus, stored.Status);
        Assert.Null(stored.DispensedBy);
    }

    [Theory]
    [InlineData(true, false, "prescriptionId", "Prescription ID must be provided.")]
    [InlineData(false, true, "doctorId", "Doctor ID must be provided.")]
    public async Task AddMedicineRejectsEmptyIdentifiers(
        bool emptyPrescriptionId,
        bool emptyDoctorId,
        string parameterName,
        string message)
    {
        await using var scope = await TestScope.CreateAsync();
        var prescription = NewPrescription(LowerId);
        await scope.SeedAsync(prescription);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.AddMedicineAsync(
                emptyPrescriptionId ? Guid.Empty : prescription.Id,
                Medicine("Cetirizine"),
                emptyDoctorId ? Guid.Empty : prescription.DoctorId));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.StartsWith(message, exception.Message);
    }

    [Fact]
    public async Task AddMedicineRejectsNullMedicine()
    {
        await using var scope = await TestScope.CreateAsync();
        var prescription = NewPrescription(LowerId);
        await scope.SeedAsync(prescription);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            scope.Service.AddMedicineAsync(prescription.Id, null!, prescription.DoctorId));
    }

    [Fact]
    public async Task AddMedicineToPrescriptionWithoutItemsStartsAtOrderZero()
    {
        await using var scope = await TestScope.CreateAsync();
        var prescription = NewPrescription(LowerId, withItem: false);
        await scope.SeedAsync(prescription);

        var result = await scope.Service.AddMedicineAsync(
            prescription.Id,
            Medicine("Cetirizine"),
            prescription.DoctorId);

        Assert.Equal(PrescriptionItemChangeOutcome.Success, result.Outcome);
        var medicine = Assert.Single(result.Prescription!.Medicines);
        Assert.Equal(0, medicine.ItemOrder);
    }

    [Theory]
    [InlineData(true, false, false, "prescriptionId", "Prescription ID must be provided.")]
    [InlineData(false, true, false, "doctorId", "Doctor ID must be provided.")]
    [InlineData(false, false, true, "medicineId", "Medicine ID must be provided.")]
    public async Task RemoveMedicineRejectsEmptyIdentifiers(
        bool emptyPrescriptionId,
        bool emptyDoctorId,
        bool emptyMedicineId,
        string parameterName,
        string message)
    {
        await using var scope = await TestScope.CreateAsync();
        var prescription = NewPrescription(LowerId, itemCount: 2);
        await scope.SeedAsync(prescription);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            scope.Service.RemoveMedicineAsync(
                emptyPrescriptionId ? Guid.Empty : prescription.Id,
                emptyMedicineId ? Guid.Empty : prescription.Items.First().Id,
                emptyDoctorId ? Guid.Empty : prescription.DoctorId));

        Assert.Equal(parameterName, exception.ParamName);
        Assert.StartsWith(message, exception.Message);
        Assert.Equal(2, await scope.DbContext.PrescriptionItems.CountAsync());
    }

    [Fact]
    public async Task RemoveMedicineFromUnknownPrescriptionReturnsNotFound()
    {
        await using var scope = await TestScope.CreateAsync();

        var result = await scope.Service.RemoveMedicineAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.Equal(PrescriptionItemChangeOutcome.PrescriptionNotFound, result.Outcome);
        Assert.Null(result.Prescription);
    }

    [Fact]
    public async Task RemoveUnknownMedicineReturnsMedicineNotFoundWithoutDeleting()
    {
        await using var scope = await TestScope.CreateAsync();
        var prescription = NewPrescription(LowerId, itemCount: 2);
        await scope.SeedAsync(prescription);

        var result = await scope.Service.RemoveMedicineAsync(
            prescription.Id,
            Guid.NewGuid(),
            prescription.DoctorId);

        Assert.Equal(PrescriptionItemChangeOutcome.MedicineNotFound, result.Outcome);
        Assert.Null(result.Prescription);
        Assert.Equal(2, await scope.DbContext.PrescriptionItems.CountAsync());
    }

    private static CreatePrescriptionRequest ValidRequest() => new()
    {
        ConsultationId = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        Medicines = [Medicine("Amoxicillin")]
    };

    private static PrescriptionItemRequest Medicine(string name) => new()
    {
        MedicineName = name,
        Dosage = "10 mg",
        Frequency = "Once daily",
        Duration = "5 days"
    };

    private static Prescription NewPrescription(
        Guid id,
        Guid? consultationId = null,
        Guid? patientId = null,
        bool withItem = true,
        int itemCount = 1)
    {
        var prescription = new Prescription
        {
            Id = id,
            ConsultationId = consultationId ?? Guid.NewGuid(),
            QueueId = Guid.NewGuid(),
            PatientId = patientId ?? Guid.NewGuid(),
            DoctorId = Guid.NewGuid(),
            DoctorName = "Dr. Amara Chen",
            Status = Prescription.PendingStatus,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        };

        if (withItem)
        {
            for (var order = 0; order < itemCount; order++)
            {
                prescription.Items.Add(new PrescriptionItem
                {
                    Id = Guid.NewGuid(),
                    ItemOrder = order,
                    MedicineName = $"Medicine {order}",
                    Dosage = "10 mg",
                    Frequency = "Once daily",
                    Duration = "5 days"
                });
            }
        }

        return prescription;
    }

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

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
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
            Service = new PrescriptionManagementService(dbContext, new FixedTimeProvider());
        }

        public PrescriptionDbContext DbContext { get; }

        public PrescriptionManagementService Service { get; }

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

        public async Task SeedAsync(params Prescription[] prescriptions)
        {
            await using var seedContext = await CreateDbContextAsync(_connection);
            seedContext.Prescriptions.AddRange(prescriptions);
            await seedContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
