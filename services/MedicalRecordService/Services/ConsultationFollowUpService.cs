using MedicalRecordService.Data;
using MedicalRecordService.Models.Configuration;
using MedicalRecordService.Models.Dtos;
using Microsoft.Extensions.Options;

namespace MedicalRecordService.Services;

public sealed class ConsultationFollowUpService : IConsultationFollowUpService
{
    private readonly IConsultationFollowUpRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _clinicTimeZone;

    public ConsultationFollowUpService(
        IConsultationFollowUpRepository repository,
        IOptions<MedicalRecordOptions> options,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
    }

    public async Task<OverdueFollowUpResponse?> FindOverdueAsync(
        Guid patientId,
        CancellationToken cancellationToken = default)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("Patient ID must be provided.", nameof(patientId));
        }

        var followUp = await _repository.FindLatestCompletedAsync(patientId, cancellationToken);
        if (followUp?.FollowUpDate is not DateOnly followUpDate
            || string.IsNullOrWhiteSpace(followUp.Instructions))
        {
            return null;
        }

        var clinicNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _clinicTimeZone);
        var clinicDate = DateOnly.FromDateTime(clinicNow.DateTime);
        if (followUpDate >= clinicDate)
        {
            return null;
        }

        return new OverdueFollowUpResponse(
            followUp.ConsultationId,
            followUpDate,
            followUp.Instructions);
    }
}
