using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using PatientService.Models.Configuration;
using PatientService.Models.Validation;
using PatientService.Services;

namespace PatientService.UnitTests.Models;

public class BirthDateClinicBoundaryTests
{
    [Theory]
    [InlineData(2026, true)]
    [InlineData(1896, true)]
    [InlineData(1895, false)]
    public void BirthDateUsesClinicDayBeforeUtcMidnight(int year, bool valid)
    {
        var provider = new ClinicDateProvider(new FixedClock(),
            Options.Create(new ClinicOptions { TimeZoneId = "Asia/Colombo" }));
        var context = new ValidationContext(new object()) { MemberName = "DateOfBirth" };
        context.InitializeServiceProvider(type => type == typeof(IClinicDateProvider) ? provider : null);

        var result = new PastDateAttribute().GetValidationResult(new DateOnly(year, 10, 10), context);

        Assert.Equal(valid, result is null);
        Assert.NotNull(new PastDateAttribute().GetValidationResult(new DateOnly(2026, 10, 11), context));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 20, 30, 0, TimeSpan.Zero);
    }
}
