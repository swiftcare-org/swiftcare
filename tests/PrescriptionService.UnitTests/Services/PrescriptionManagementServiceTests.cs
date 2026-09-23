using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PrescriptionService.Data;
using PrescriptionService.Models;
using PrescriptionService.Models.Dtos;
using PrescriptionService.Models.Entities;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Services;

public class PrescriptionManagementServiceTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 9, 23, 8, 30, 0, TimeSpan.Zero);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    [Fact]
    public async Task CreateStoresLinksDoctorPendingStatusAndOrderedMedicines()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var request = ValidRequest();
        var doctorId = Guid.NewGuid();
        var service = new PrescriptionManagementService(
            dbContext,
            new MutableTimeProvider(InitialTime));

        var result = await service.CreateAsync(request, doctorId, "  Dr. Amara Chen  ");

        Assert.Equal(CreatePrescriptionOutcome.Success, result.Outcome);
        Assert.NotNull(result.Prescription);
        Assert.Equal(request.ConsultationId, result.Prescription.ConsultationId);
        Assert.Equal(request.QueueId, result.Prescription.QueueId);
        Assert.Equal(request.PatientId, result.Prescription.PatientId);
        Assert.Equal(doctorId, result.Prescription.DoctorId);
        Assert.Equal("Dr. Amara Chen", result.Prescription.DoctorName);
        Assert.Equal(Prescription.PendingStatus, result.Prescription.Status);
        Assert.Equal(InitialTime.UtcDateTime, result.Prescription.CreatedAt);

        var stored = await dbContext.Prescriptions
            .Include(prescription => prescription.Items)
            .SingleAsync();
        Assert.Equal(Prescription.PendingStatus, stored.Status);
        Assert.Equal([0, 1], stored.Items.OrderBy(item => item.ItemOrder).Select(item => item.ItemOrder));
        Assert.Equal(
            ["Amoxicillin", "Paracetamol"],
            stored.Items.OrderBy(item => item.ItemOrder).Select(item => item.MedicineName));
        Assert.Null(stored.Items.Single(item => item.ItemOrder == 1).Instructions);
    }

    [Fact]
    public async Task DuplicateConsultationDoesNotCreateSecondPrescription()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var request = ValidRequest();
        var service = new PrescriptionManagementService(
            dbContext,
            new MutableTimeProvider(InitialTime));

        var first = await service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen");
        var duplicate = await service.CreateAsync(request, Guid.NewGuid(), "Dr. Priya Rao");

        Assert.Equal(CreatePrescriptionOutcome.Success, first.Outcome);
        Assert.Equal(
            CreatePrescriptionOutcome.ConsultationAlreadyHasPrescription,
            duplicate.Outcome);
        Assert.Equal(1, await dbContext.Prescriptions.CountAsync());
        Assert.Equal(2, await dbContext.PrescriptionItems.CountAsync());
    }

    [Fact]
    public async Task GetForPatientReturnsNewestFirstWithOrderedMedicines()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var patientId = Guid.NewGuid();
        var timeProvider = new MutableTimeProvider(InitialTime);
        var service = new PrescriptionManagementService(dbContext, timeProvider);
        var olderRequest = ValidRequest(patientId);
        await service.CreateAsync(olderRequest, Guid.NewGuid(), "Dr. Amara Chen");
        timeProvider.UtcNow = InitialTime.AddDays(1);
        var newerRequest = ValidRequest(patientId);
        await service.CreateAsync(newerRequest, Guid.NewGuid(), "Dr. Priya Rao");
        await service.CreateAsync(ValidRequest(Guid.NewGuid()), Guid.NewGuid(), "Dr. Other");

        var history = await service.GetForPatientAsync(patientId);

        Assert.Collection(
            history,
            prescription =>
            {
                Assert.Equal(newerRequest.ConsultationId, prescription.ConsultationId);
                Assert.Equal([0, 1], prescription.Medicines.Select(item => item.ItemOrder));
            },
            prescription =>
                Assert.Equal(olderRequest.ConsultationId, prescription.ConsultationId));
    }

    [Fact]
    public async Task GetForPatientWithoutHistoryReturnsEmptyCollection()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var service = new PrescriptionManagementService(
            dbContext,
            new MutableTimeProvider(InitialTime));

        var history = await service.GetForPatientAsync(Guid.NewGuid());

        Assert.Empty(history);
    }

    [Fact]
    public async Task CreateWithNoMedicinesIsRejectedBeforePersistence()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        var request = ValidRequest();
        request.Medicines.Clear();
        var service = new PrescriptionManagementService(
            dbContext,
            new MutableTimeProvider(InitialTime));

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(request, Guid.NewGuid(), "Dr. Amara Chen"));

        Assert.Contains("Add at least one medicine", exception.Message);
        Assert.Empty(await dbContext.Prescriptions.ToListAsync());
    }

    private static CreatePrescriptionRequest ValidRequest(Guid? patientId = null) => new()
    {
        ConsultationId = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = patientId ?? Guid.NewGuid(),
        Medicines =
        [
            new PrescriptionItemRequest
            {
                MedicineName = " Amoxicillin ",
                Dosage = " 500 mg ",
                Frequency = " Twice daily ",
                Duration = " 5 days ",
                Instructions = " After meals "
            },
            new PrescriptionItemRequest
            {
                MedicineName = "Paracetamol",
                Dosage = "500 mg",
                Frequency = "When required",
                Duration = "3 days",
                Instructions = "   "
            }
        ]
    };

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }

    private static async Task<PrescriptionDbContext> CreateDbContextAsync(
        SqliteConnection connection)
    {
        var dbContext = new PrescriptionDbContext(
            new DbContextOptionsBuilder<PrescriptionDbContext>()
                .UseSqlite(connection)
                .Options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }
}
