using PatientService.Logging;

namespace PatientService.UnitTests.Logging;

public class LogSanitizerTests
{
    [Theory]
    [InlineData("corr-123", "corr-123")]
    [InlineData("corr-123\r\nFORGED LOG LINE: admin login succeeded", "corr-123FORGED LOG LINE: admin login succeeded")]
    [InlineData("corr\r-\n123", "corr-123")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void SanitizeStripsCarriageReturnsAndLineFeeds(string? input, string expected)
    {
        Assert.Equal(expected, LogSanitizer.Sanitize(input));
    }
}
