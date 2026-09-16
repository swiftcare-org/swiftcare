using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MedicalRecordService.Data;

// Used only by `dotnet ef` design-time commands. A static server version keeps
// migration scaffolding independent from a running MySQL instance.
public sealed class MedicalRecordDbContextFactory
    : IDesignTimeDbContextFactory<MedicalRecordDbContext>
{
    public MedicalRecordDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MedicalRecordDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=swiftcare_medical_record;User=design_time;Password=design_time;",
            new MySqlServerVersion(new Version(8, 4, 0)));

        return new MedicalRecordDbContext(optionsBuilder.Options);
    }
}
