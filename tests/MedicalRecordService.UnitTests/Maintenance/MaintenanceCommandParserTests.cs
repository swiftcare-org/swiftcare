using MedicalRecordService.Maintenance;

namespace MedicalRecordService.UnitTests.Maintenance;

public class MaintenanceCommandParserTests
{
    [Theory]
    [InlineData("--migrate")]
    [InlineData("--MIGRATE")]
    public void Parse_RecognizesMigrateFlag(string argument)
    {
        Assert.Equal(
            MaintenanceCommand.Migrate,
            MaintenanceCommandParser.Parse([argument]));
    }

    [Fact]
    public void Parse_ReturnsNone_WhenMigrateFlagIsMissing()
    {
        Assert.Equal(MaintenanceCommand.None, MaintenanceCommandParser.Parse([]));
        Assert.Equal(
            MaintenanceCommand.None,
            MaintenanceCommandParser.Parse(["--urls", "http://+:5004"]));
    }

    [Fact]
    public void Parse_FindsMigrateFlagAmongOtherArguments()
    {
        Assert.Equal(
            MaintenanceCommand.Migrate,
            MaintenanceCommandParser.Parse(["--environment", "Production", "--migrate"]));
    }
}
