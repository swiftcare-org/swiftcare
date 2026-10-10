using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MySqlConnector;
using PrescriptionService.Data;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Services;

public class PrescriptionConcurrencyMySqlTests
{
    [MySqlPrescriptionFact]
    public async Task PrescriptionAndNoPrescriptionCannotBothCommit()
    {
        await using var database = await DatabaseScope.CreateAsync();
        var barrier = new SaveBarrier(2);
        await using var first = database.Context(barrier);
        await using var second = database.Context(barrier);
        var request = Request();
        var prescriptionTask = new PrescriptionManagementService(first, TimeProvider.System)
            .CreateAsync(request, Guid.NewGuid(), "Dr. Test");
        var decisionTask = new NoPrescriptionService(second, TimeProvider.System)
            .RecordAsync(request.ConsultationId, new RecordNoPrescriptionRequest
            { QueueId = request.QueueId, PatientId = request.PatientId }, Guid.NewGuid(), "Dr. Test");

        await Task.WhenAll(prescriptionTask, decisionTask).WaitAsync(TimeSpan.FromSeconds(30));

        await using var verify = database.Context();
        Assert.Equal(1, await verify.ConsultationOutcomes.CountAsync());
        Assert.Equal(1, await verify.Prescriptions.CountAsync() + await verify.NoPrescriptionDecisions.CountAsync());
        Assert.True(prescriptionTask.Result.Outcome == CreatePrescriptionOutcome.Success
            || decisionTask.Result.Outcome == RecordNoPrescriptionOutcome.Recorded);
    }

    [MySqlPrescriptionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleMedicineEditCannotCommitAfterDispensing(bool remove)
    {
        await using var database = await DatabaseScope.CreateAsync();
        var doctor = Guid.NewGuid();
        var prescription = await SeedAsync(database, doctor);
        var pause = new SavePause();
        await using var editing = database.Context(pause);
        await using var dispensing = database.Context();
        var service = new PrescriptionManagementService(editing, TimeProvider.System);
        var editTask = remove
            ? service.RemoveMedicineAsync(prescription.Id, prescription.Medicines[0].Id, doctor)
            : service.AddMedicineAsync(prescription.Id, Medicine("Third"), doctor);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var dispense = await new PrescriptionManagementService(dispensing, TimeProvider.System)
            .DispenseAsync(prescription.Id, "Receptionist A");
        pause.Resume.SetResult();
        var edit = await editTask.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(DispensePrescriptionOutcome.Success, dispense.Outcome);
        Assert.Equal(PrescriptionItemChangeOutcome.ConcurrentModification, edit.Outcome);
        await using var verify = database.Context();
        Assert.Equal(2, await verify.PrescriptionItems.CountAsync());
        Assert.Equal("Receptionist A", (await verify.Prescriptions.SingleAsync()).DispensedBy);
    }

    [MySqlPrescriptionFact]
    public async Task ConcurrentDispensersCannotOverwriteAttribution()
    {
        await using var database = await DatabaseScope.CreateAsync();
        var prescription = await SeedAsync(database, Guid.NewGuid());
        var barrier = new SaveBarrier(2);
        await using var first = database.Context(barrier);
        await using var second = database.Context(barrier);
        var tasks = new[]
        {
            new PrescriptionManagementService(first, TimeProvider.System).DispenseAsync(prescription.Id, "Receptionist A"),
            new PrescriptionManagementService(second, TimeProvider.System).DispenseAsync(prescription.Id, "Receptionist B")
        };
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));

        var winner = Assert.Single(results, result => result.Outcome == DispensePrescriptionOutcome.Success);
        Assert.Single(results, result => result.Outcome == DispensePrescriptionOutcome.ConcurrentModification);
        await using var verify = database.Context();
        var stored = await verify.Prescriptions.SingleAsync();
        Assert.Equal(winner.Prescription!.DispensedBy, stored.DispensedBy);
        Assert.Equal(winner.Prescription.DispensedAt!.Value.Ticks / 10, stored.DispensedAt!.Value.Ticks / 10);
    }

    private static CreatePrescriptionRequest Request() => new()
    {
        ConsultationId = Guid.NewGuid(), QueueId = Guid.NewGuid(), PatientId = Guid.NewGuid(),
        Medicines = [Medicine("First"), Medicine("Second")]
    };

    private static PrescriptionItemRequest Medicine(string name) => new()
    { MedicineName = name, Dosage = "1", Frequency = "Daily", Duration = "1 day" };

    private static async Task<PrescriptionService.Models.Dtos.PrescriptionResponse> SeedAsync(DatabaseScope database, Guid doctor)
    {
        await using var context = database.Context();
        var result = await new PrescriptionManagementService(context, TimeProvider.System).CreateAsync(Request(), doctor, "Dr. Test");
        return result.Prescription!;
    }

    private sealed class SaveBarrier(int participants) : SaveChangesInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _arrivals) == participants) _ready.SetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }

    private sealed class SavePause : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Reached.SetResult();
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }

    private sealed class DatabaseScope(MySqlConnection admin, string database, string connectionString) : IAsyncDisposable
    {
        public PrescriptionDbContext Context(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<PrescriptionDbContext>()
                .UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0)));
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new PrescriptionDbContext(options.Options);
        }

        public static async Task<DatabaseScope> CreateAsync()
        {
            var settings = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")!);
            var database = "swc_pc_" + Guid.NewGuid().ToString("N");
            var admin = new MySqlConnection(settings.ConnectionString);
            await admin.OpenAsync();
            await using (var create = new MySqlCommand($"CREATE DATABASE `{database}`", admin)) await create.ExecuteNonQueryAsync();
            settings.Database = database;
            var scope = new DatabaseScope(admin, database, settings.ConnectionString);
            await using var context = scope.Context();
            await context.Database.MigrateAsync();
            return scope;
        }

        public async ValueTask DisposeAsync()
        {
            await using var drop = new MySqlCommand($"DROP DATABASE `{database}`", admin);
            await drop.ExecuteNonQueryAsync();
            await admin.DisposeAsync();
        }
    }
}

public sealed class MySqlPrescriptionFactAttribute : FactAttribute
{
    public MySqlPrescriptionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")))
            Skip = "Set SWIFTCARE_TEST_MYSQL to an isolated MySQL server to run concurrency verification.";
    }
}

public sealed class MySqlPrescriptionTheoryAttribute : TheoryAttribute
{
    public MySqlPrescriptionTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")))
            Skip = "Set SWIFTCARE_TEST_MYSQL to an isolated MySQL server to run concurrency verification.";
    }
}
