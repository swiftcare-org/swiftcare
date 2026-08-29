using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QueueService.Data;

// Used only by `dotnet ef` design-time commands (migrations add/remove).
// A static server version avoids AutoDetect connecting to a live database
// just to scaffold a migration - the real connection string is resolved
// from configuration at runtime in Program.cs instead.
public sealed class QueueDbContextFactory : IDesignTimeDbContextFactory<QueueDbContext>
{
    public QueueDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QueueDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=swiftcare_queue;User=design_time;Password=design_time;",
            new MySqlServerVersion(new Version(8, 4, 0)));

        return new QueueDbContext(optionsBuilder.Options);
    }
}
