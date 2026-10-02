using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrescriptionService.Data;
using PrescriptionService.Models.Configuration;
using PrescriptionService.Models.Entities;
using PrescriptionService.Services;

namespace PrescriptionService.UnitTests.Services;

// SWC-142: the daily prescription report used by the SWC-31 daily summary.
public class PrescriptionReportServiceTests
{
    // Asia/Colombo is UTC+05:30, so 2 October there runs from 1 Oct 18:30 UTC to 2 Oct 18:30 UTC.
    private const string ClinicTimeZone = "Asia/Colombo";
    private const string Pending = Prescription.PendingStatus;
    private const string Dispensed = Prescription.DispensedStatus;
    private static readonly DateOnly ReportDate = new(2026, 10, 2);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Fact]
    public async Task DailyReportCountsWrittenDispensedAndPendingForTheDay()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Prescriptions.AddRange(
            NewPrescription(Utc(2026, 10, 2, 3, 0), Pending),
            NewPrescription(Utc(2026, 10, 2, 4, 0), Dispensed),
            NewPrescription(Utc(2026, 10, 2, 5, 0), Dispensed));
        await dbContext.SaveChangesAsync();

        var report = await CreateService(dbContext).GetDailyReportAsync(ReportDate);

        Assert.Equal(ReportDate, report.Date);
        Assert.Equal(3, report.TotalWritten);
        Assert.Equal(2, report.TotalDispensed);
        Assert.Equal(1, report.TotalPending);
    }

    [Fact]
    public async Task DailyReportIsAllZeroWhenNothingWasWritten()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);

        var report = await CreateService(dbContext).GetDailyReportAsync(ReportDate);

        Assert.Equal(ReportDate, report.Date);
        Assert.Equal(0, report.TotalWritten);
        Assert.Equal(0, report.TotalDispensed);
        Assert.Equal(0, report.TotalPending);
    }

    [Fact]
    public async Task DailyReportUsesTheClinicDayNotTheUtcDay()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Prescriptions.AddRange(
            // 1 Oct 18:29 UTC is 23:59 on 1 Oct in the clinic: the day before.
            NewPrescription(Utc(2026, 10, 1, 18, 29), Pending),
            // 1 Oct 18:30 UTC is 00:00 on 2 Oct in the clinic: the first moment of the day.
            NewPrescription(Utc(2026, 10, 1, 18, 30), Pending),
            // 2 Oct 18:29 UTC is 23:59 on 2 Oct in the clinic: still the day.
            NewPrescription(Utc(2026, 10, 2, 18, 29), Dispensed),
            // 2 Oct 18:30 UTC is 00:00 on 3 Oct in the clinic: the day after.
            NewPrescription(Utc(2026, 10, 2, 18, 30), Dispensed));
        await dbContext.SaveChangesAsync();

        var report = await CreateService(dbContext).GetDailyReportAsync(ReportDate);

        Assert.Equal(2, report.TotalWritten);
        Assert.Equal(1, report.TotalDispensed);
        Assert.Equal(1, report.TotalPending);
    }

    [Fact]
    public async Task DailyReportWithoutADateUsesTodayInTheClinic()
    {
        using var connection = OpenConnection();
        await using var dbContext = await CreateDbContextAsync(connection);
        dbContext.Prescriptions.Add(
            NewPrescription(Utc(2026, 10, 1, 19, 0), Pending));
        await dbContext.SaveChangesAsync();
        // 1 Oct 20:00 UTC is already 01:30 on 2 Oct in the clinic.
        var now = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

        var report = await CreateService(dbContext, now).GetDailyReportAsync(date: null);

        Assert.Equal(ReportDate, report.Date);
        Assert.Equal(1, report.TotalWritten);
    }

    private static PrescriptionReportService CreateService(
        PrescriptionDbContext dbContext,
        DateTimeOffset? now = null) => new(
            dbContext,
            Options.Create(new ClinicOptions { TimeZone = ClinicTimeZone }),
            new FixedTimeProvider(now ?? new DateTimeOffset(2026, 10, 2, 6, 0, 0, TimeSpan.Zero)));

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static Prescription NewPrescription(DateTime createdAt, string status) => new()
    {
        Id = Guid.NewGuid(),
        ConsultationId = Guid.NewGuid(),
        QueueId = Guid.NewGuid(),
        PatientId = Guid.NewGuid(),
        DoctorId = Guid.NewGuid(),
        DoctorName = "Dr. Amara Chen",
        Status = status,
        CreatedAt = createdAt,
        UpdatedAt = createdAt
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
