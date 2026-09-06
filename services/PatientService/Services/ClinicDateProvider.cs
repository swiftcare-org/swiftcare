using Microsoft.Extensions.Options;
using PatientService.Models.Configuration;

namespace PatientService.Services;

public sealed class ClinicDateProvider : IClinicDateProvider
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _clinicTimeZone;

    public ClinicDateProvider(
        TimeProvider timeProvider,
        IOptions<ClinicOptions> options)
    {
        _timeProvider = timeProvider;
        _clinicTimeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);
    }

    public DateOnly Today
    {
        get
        {
            var clinicNow = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _clinicTimeZone);
            return DateOnly.FromDateTime(clinicNow.DateTime);
        }
    }
}
