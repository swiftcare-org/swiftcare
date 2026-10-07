using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotificationService.Data;

// Used only by `dotnet ef` design-time commands (migrations add/remove).
// A static server version avoids AutoDetect connecting to a live database
// just to scaffold a migration - the real connection string is resolved
// from configuration at runtime in Program.cs instead.
public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotificationDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=swiftcare_notification;User=design_time;Password=design_time;",
            new MySqlServerVersion(new Version(8, 4, 0)));

        return new NotificationDbContext(optionsBuilder.Options);
    }
}
