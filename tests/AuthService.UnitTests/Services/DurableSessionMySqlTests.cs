using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;

namespace AuthService.UnitTests.Services;

public sealed class DurableSessionMySqlTests
{
    [MySqlFact]
    public async Task MigrationBackfillsExistingAccountsAndLogoutSurvivesANewContext()
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")!);
        builder.Database = "";
        await using var admin = new MySqlConnection(builder.ConnectionString);
        await admin.OpenAsync();
        var name = "session_" + Guid.NewGuid().ToString("N");
        await using (var create = new MySqlCommand($"CREATE DATABASE `{name}`", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            builder.Database = name;
            var options = new DbContextOptionsBuilder<AuthDbContext>()
                .UseMySql(builder.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))).Options;
            var userId = Guid.NewGuid();
            await using (var context = new AuthDbContext(options))
            {
                var previous = context.Database.GetMigrations().Last(migration => !migration.EndsWith("AddDurableSessions"));
                await context.GetService<IMigrator>().MigrateAsync(previous);
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO Users (Id, Username, PasswordHash, FullName, Role, IsActive, IsDeleted, CreatedAt, UpdatedAt)
                    VALUES ({userId.ToString()}, 'existing-user', 'hash', 'Existing User', 'Doctor', TRUE, FALSE, UTC_TIMESTAMP(), UTC_TIMESTAMP())
                    """);
                await context.Database.MigrateAsync();
            }
            SessionValidationRequest request;
            await using (var context = new AuthDbContext(options))
            {
                var existing = await context.Users.SingleAsync();
                Assert.NotEqual(Guid.Empty, existing.SessionVersion);
                request = new(existing.Id, existing.SessionVersion, Guid.NewGuid().ToString(),
                    DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), true);
                Assert.True(await new SessionValidationService(context, TimeProvider.System).ValidateAsync(request, CancellationToken.None));
            }
            await using var restarted = new AuthDbContext(options);
            Assert.False(await new SessionValidationService(restarted, TimeProvider.System)
                .ValidateAsync(request with { Revoke = false }, CancellationToken.None));
            Assert.Equal(request.TokenId, (await restarted.RevokedSessions.SingleAsync()).TokenId);
        }
        finally
        {
            await using var drop = new MySqlCommand($"DROP DATABASE IF EXISTS `{name}`", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class MySqlFactAttribute : FactAttribute
    {
        public MySqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWIFTCARE_TEST_MYSQL")))
                Skip = "Set SWIFTCARE_TEST_MYSQL to run isolated database regressions.";
        }
    }
}
