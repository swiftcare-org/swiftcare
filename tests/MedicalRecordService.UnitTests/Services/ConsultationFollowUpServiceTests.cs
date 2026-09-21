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
                "Review blood pressure"));

        var result = await CreateService(repository).FindOverdueAsync(patientId);

        Assert.NotNull(result);
        Assert.Equal(consultationId, result.ConsultationId);
        Assert.Equal(new DateOnly(2026, 9, 21), result.FollowUpDate);
        Assert.Equal("Review blood pressure", result.Instructions);
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
                "Review blood pressure"));

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
                instructions));

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
            Options.Create(new MedicalRecordOptions { ClinicTimeZone = "Asia/Colombo" }),
            new FixedTimeProvider());
}
