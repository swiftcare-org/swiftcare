using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PatientService.Data;

// Used only by `dotnet ef` design-time commands (migrations add/remove).
// A static server version avoids AutoDetect connecting to a live database
// just to scaffold a migration - the real connection string is resolved
// from configuration at runtime in Program.cs instead.
public sealed class PatientDbContextFactory : IDesignTimeDbContextFactory<PatientDbContext>
{
    public PatientDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PatientDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=swiftcare_patient;User=design_time;Password=design_time;",
            new MySqlServerVersion(new Version(8, 4, 0)));

        return new PatientDbContext(optionsBuilder.Options);
    }
}
