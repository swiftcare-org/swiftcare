namespace PatientService.Maintenance;

public enum MaintenanceCommand
{
    None,
    Migrate
}

public static class MaintenanceCommandParser
{
    public const string MigrateFlag = "--migrate";

    public static MaintenanceCommand Parse(string[] args)
    {
        return args.Any(arg => string.Equals(arg, MigrateFlag, StringComparison.OrdinalIgnoreCase))
            ? MaintenanceCommand.Migrate
            : MaintenanceCommand.None;
    }
}
