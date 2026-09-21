using MedicalRecordService.Models.Configuration;
using MedicalRecordService.UnitTests.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedicalRecordService.UnitTests.Configuration;

public class MedicalRecordOptionsTests
{
    [Fact]
    public void ApplicationConfigurationBindsClinicTimeZone()
    {
        using var factory = new MedicalRecordServiceWebApplicationFactory();

        var options = factory.Services
            .GetRequiredService<IOptions<MedicalRecordOptions>>()
            .Value;

        Assert.Equal("Asia/Colombo", options.TimeZone);
    }
}
