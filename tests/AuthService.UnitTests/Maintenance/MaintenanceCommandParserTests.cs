using AuthService.Maintenance;

namespace AuthService.UnitTests.Maintenance;

public class MaintenanceCommandParserTests
{
    [Theory]
    [InlineData("--migrate", MaintenanceCommand.Migrate)]
    [InlineData("--MIGRATE", MaintenanceCommand.Migrate)]
    [InlineData("--bootstrap-admin", MaintenanceCommand.BootstrapAdmin)]
    [InlineData("--BOOTSTRAP-ADMIN", MaintenanceCommand.BootstrapAdmin)]
    public void Parse_RecognizesCommandFlags(string arg, MaintenanceCommand expected)
    {
        Assert.Equal(expected, MaintenanceCommandParser.Parse([arg]));
    }

    [Fact]
    public void Parse_ReturnsNone_WhenNoCommandFlagPresent()
    {
        Assert.Equal(MaintenanceCommand.None, MaintenanceCommandParser.Parse([]));
        Assert.Equal(MaintenanceCommand.None, MaintenanceCommandParser.Parse(["--urls", "http://+:5000"]));
    }

    [Fact]
    public void Parse_FindsCommandFlagAmongOtherArguments()
    {
        Assert.Equal(
            MaintenanceCommand.Migrate,
            MaintenanceCommandParser.Parse(["--environment", "Production", "--migrate"]));
    }
}
