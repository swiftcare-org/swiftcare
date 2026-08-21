using AuthService.Models.Entities;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

// Development-only. Seeds synthetic accounts so login can be demoed locally
// without a real user-provisioning flow. Never runs outside Development,
// and never generates or hardcodes a password - it hashes whatever is
// supplied via AUTH_SEED_PASSWORD.
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(
        AuthDbContext dbContext,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var seedPassword = configuration["AUTH_SEED_PASSWORD"];
        if (string.IsNullOrWhiteSpace(seedPassword))
        {
            logger.LogWarning("AUTH_SEED_PASSWORD is not set; skipping development user seeding.");
            return;
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword);

        dbContext.Users.AddRange(
            new User
            {
                Username = "dr.chen",
                PasswordHash = passwordHash,
                FullName = "Dr. Amara Chen",
                Role = UserRole.Doctor,
                RoomNumber = "R-204",
                IsActive = true
            },
            new User
            {
                Username = "reception.silva",
                PasswordHash = passwordHash,
                FullName = "Nadia Silva",
                Role = UserRole.Receptionist,
                RoomNumber = null,
                IsActive = true
            },
            new User
            {
                Username = "admin.fernando",
                PasswordHash = passwordHash,
                FullName = "Ravi Fernando",
                Role = UserRole.Admin,
                RoomNumber = null,
                IsActive = true
            },
            new User
            {
                Username = "dr.rao",
                PasswordHash = passwordHash,
                FullName = "Dr. Priya Rao",
                Role = UserRole.Doctor,
                RoomNumber = "R-108",
                IsActive = false
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded development users: userCount={UserCount}", 4);
    }
}
