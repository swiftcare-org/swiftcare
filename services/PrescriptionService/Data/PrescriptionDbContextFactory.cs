using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PrescriptionService.Data;

// Used by design-time EF commands without connecting to a live database.
public sealed class PrescriptionDbContextFactory
    : IDesignTimeDbContextFactory<PrescriptionDbContext>
{
    public PrescriptionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PrescriptionDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=swiftcare_prescription;User=design_time;Password=design_time;",
            new MySqlServerVersion(new Version(8, 4, 0)));

        return new PrescriptionDbContext(optionsBuilder.Options);
    }
}
