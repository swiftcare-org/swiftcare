using AuthService.Data;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using AuthService.Services;
using Microsoft.EntityFrameworkCore;

namespace AuthService.UnitTests.Services;

// The limit keeps the newest sign-outs and admin actions, not only the newest sign-ins
// (SWC-151 mutation testing).
public class AuditLogServiceEdgeCaseTests
{
    private static readonly DateTime BaseTime = new(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("logout")]
    [InlineData("admin")]
    public async Task LimitKeepsTheNewestEntriesOfEachSource(string source)
    {
        await using var dbContext = new AuthDbContext(new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var userId = Guid.NewGuid();
        for (var minute = 1; minute <= 3; minute++)
        {
            if (source == "logout")
            {
                dbContext.LogoutAuditEntries.Add(new LogoutAuditEntry
                {
                    UserId = userId,
                    CorrelationId = "correlation",
                    IpAddress = "10.0.0.2",
                    OccurredAt = BaseTime.AddMinutes(minute)
                });
            }
            else
            {
                dbContext.AdminAuditEntries.Add(new AdminAuditEntry
                {
                    ActorUserId = userId,
                    TargetUserId = userId,
                    Action = AdminAuditAction.UserUpdated,
                    CorrelationId = "correlation",
                    IpAddress = "10.0.0.3",
                    OccurredAt = BaseTime.AddMinutes(minute)
                });
            }
        }

        await dbContext.SaveChangesAsync();

        var entries = await new AuditLogService(dbContext).GetRecentAsync(limit: 2);

        Assert.Equal(new[] { BaseTime.AddMinutes(3), BaseTime.AddMinutes(2) }, entries.Select(entry => entry.OccurredAt));
    }
}
