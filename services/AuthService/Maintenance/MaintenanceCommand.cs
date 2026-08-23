namespace AuthService.Maintenance;

// Finite commands the image can run instead of serving HTTP. Azure Container Apps Jobs
// need a process that terminates with a meaningful exit code, not a web host.
public enum MaintenanceCommand
{
    None,
    Migrate,
    BootstrapAdmin
}

public static class MaintenanceCommandParser
{
    public const string MigrateFlag = "--migrate";
    public const string BootstrapAdminFlag = "--bootstrap-admin";

    public static MaintenanceCommand Parse(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, MigrateFlag, StringComparison.OrdinalIgnoreCase))
            {
                return MaintenanceCommand.Migrate;
            }

            if (string.Equals(arg, BootstrapAdminFlag, StringComparison.OrdinalIgnoreCase))
            {
                return MaintenanceCommand.BootstrapAdmin;
            }
        }

        return MaintenanceCommand.None;
    }
}
