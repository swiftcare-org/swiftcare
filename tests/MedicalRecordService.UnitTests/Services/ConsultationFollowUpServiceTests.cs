using MedicalRecordService.Data;
using MedicalRecordService.Models.Configuration;
using MedicalRecordService.Models.Entities;
using MedicalRecordService.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace MedicalRecordService.UnitTests.Services;

public class ConsultationFollowUpServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 21, 20, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtcNow;
    }

    [Fact]
    public async Task FindOverdueReturnsLatestCompletedFollowUpBeforeClinicDate()
    {
        var patientId = Guid.NewGuid();
        var consultationId = Guid.NewGuid();
        var repository = new Mock<IConsultationFollowUpRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.FindLatestCompletedAsync(
                patientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationFollowUp(
                consultationId,
                new DateOnly(2026, 9, 21),
                "Review blood pressure",
                "Dr. Amara Chen"));

        var result = await CreateService(repository).FindOverdueAsync(patientId);

        Assert.NotNull(result);
        Assert.Equal(consultationId, result.ConsultationId);
        Assert.Equal(new DateOnly(2026, 9, 21), result.FollowUpDate);
        Assert.Equal("Review blood pressure", result.Instructions);
        Assert.Equal("Dr. Amara Chen", result.DoctorName);
        Assert.Equal(1, result.DaysOverdue);
        repository.VerifyAll();
    }

    // The clinic date is 22 Sep 2026 (FixedUtcNow is 01:30 on 22 Sep in Asia/Colombo), so
    // the count is taken in clinic days, not UTC days.
    [Theory]
    [InlineData(21, 1)]
    [InlineData(15, 7)]
    [InlineData(1, 21)]
    public async Task FindOverdueCountsWholeClinicDaysSinceTheFollowUpDate(int day, int expectedDays)
    {
        var patientId = Guid.NewGuid();
        var repository = new Mock<IConsultationFollowUpRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.FindLatestCompletedAsync(
                patientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationFollowUp(
                Guid.NewGuid(),
                new DateOnly(2026, 9, day),
                "Review blood pressure in 6 weeks",
                "Dr. Silva"));

        var result = await CreateService(repository).FindOverdueAsync(patientId);

        Assert.NotNull(result);
        Assert.Equal(expectedDays, result.DaysOverdue);
        Assert.Equal("Dr. Silva", result.DoctorName);
        Assert.Equal("Review blood pressure in 6 weeks", result.Instructions);
        repository.VerifyAll();
    }

    [Theory]
    [InlineData(22)]
    [InlineData(23)]
    public async Task FindOverdueReturnsNullForTodayOrFutureDate(int day)
    {
        var patientId = Guid.NewGuid();
        var repository = new Mock<IConsultationFollowUpRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.FindLatestCompletedAsync(
                patientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationFollowUp(
                Guid.NewGuid(),
                new DateOnly(2026, 9, day),
                "Review blood pressure",
                "Dr. Amara Chen"));

        var result = await CreateService(repository).FindOverdueAsync(patientId);

        Assert.Null(result);
        repository.VerifyAll();
    }

    [Theory]
    [InlineData(false, "Review blood pressure")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    public async Task FindOverdueReturnsNullWhenFollowUpDetailsAreIncomplete(
        bool includeDate,
        string? instructions)
    {
        var patientId = Guid.NewGuid();
        var repository = new Mock<IConsultationFollowUpRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.FindLatestCompletedAsync(
                patientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsultationFollowUp(
                Guid.NewGuid(),
                includeDate ? new DateOnly(2026, 9, 20) : null,
                instructions,
                "Dr. Amara Chen"));

        var result = await CreateService(repository).FindOverdueAsync(patientId);

        Assert.Null(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task FindOverdueReturnsNullWhenPatientHasNoCompletedConsultation()
    {
        var patientId = Guid.NewGuid();
        var repository = new Mock<IConsultationFollowUpRepository>(MockBehavior.Strict);
        repository
            .Setup(item => item.FindLatestCompletedAsync(
                patientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConsultationFollowUp?)null);

        var result = await CreateService(repository).FindOverdueAsync(patientId);

        Assert.Null(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task FindOverdueRejectsEmptyPatientIdWithoutQueryingRepository()
    {
        var repository = new Mock<IConsultationFollowUpRepository>(MockBehavior.Strict);

        var action = () => CreateService(repository).FindOverdueAsync(Guid.Empty);

        await Assert.ThrowsAsync<ArgumentException>(action);
        repository.VerifyNoOtherCalls();
    }

    private static ConsultationFollowUpService CreateService(
        Mock<IConsultationFollowUpRepository> repository) =>
        new(
            repository.Object,
            Options.Create(new MedicalRecordOptions { TimeZone = "Asia/Colombo" }),
            new FixedTimeProvider());
}
