using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Services;

// Read-only view over the three audit tables. Nothing here writes: entries are added
// only by the sign-in, sign-out and account management paths themselves.
public sealed class AuditLogService : IAuditLogService
{
    public const int DefaultLimit = 200;
    public const int MaximumLimit = 500;
    public const string UnknownUsername = "Unknown";

    public const string LoginSucceededAction = "LoginSucceeded";
    public const string LoginFailedAction = "LoginFailed";
    public const string LoginBlockedAction = "LoginBlocked";
    public const string LogoutAction = "Logout";

    private readonly AuthDbContext _dbContext;

    public AuditLogService(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AuditLogEntryResponse>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaximumLimit);

        // The newest entries overall are always among the newest of each table, so taking
        // that many from each and merging gives the right answer without reading it all.
        var logins = await _dbContext.LoginAuditEntries.AsNoTracking()
            .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(cancellationToken);
        var logouts = await _dbContext.LogoutAuditEntries.AsNoTracking()
            .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(cancellationToken);
        var adminActions = await _dbContext.AdminAuditEntries.AsNoTracking()
            .OrderByDescending(e => e.OccurredAt).Take(limit).ToListAsync(cancellationToken);

        var userIds = logins.Where(e => e.UserId.HasValue).Select(e => e.UserId!.Value)
            .Concat(logouts.Select(e => e.UserId))
            .Concat(adminActions.SelectMany(e => new[] { e.ActorUserId, e.TargetUserId }))
            .Distinct()
            .ToList();

        // Soft-deleted accounts are included: their past actions still need a name.
        var usernames = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);

        string NameOf(Guid? userId) =>
            userId.HasValue && usernames.TryGetValue(userId.Value, out var username) ? username : UnknownUsername;

        var entries = logins
            .Select(e => new AuditLogEntryResponse
            {
                Id = e.Id,
                Username = NameOf(e.UserId),
                Action = DescribeLogin(e.Outcome),
                OccurredAt = AsUtc(e.OccurredAt),
                IpAddress = e.IpAddress
            })
            .Concat(logouts.Select(e => new AuditLogEntryResponse
            {
                Id = e.Id,
                Username = NameOf(e.UserId),
                Action = LogoutAction,
                OccurredAt = AsUtc(e.OccurredAt),
                IpAddress = e.IpAddress
            }))
            .Concat(adminActions.Select(e => new AuditLogEntryResponse
            {
                Id = e.Id,
                Username = NameOf(e.ActorUserId),
                Action = e.Action.ToString(),
                TargetUsername = NameOf(e.TargetUserId),
                OccurredAt = AsUtc(e.OccurredAt),
                IpAddress = e.IpAddress
            }));

        return entries.OrderByDescending(e => e.OccurredAt).Take(limit).ToList();
    }

    private static string DescribeLogin(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.Success => LoginSucceededAction,
        LoginOutcome.AccountDeactivated => LoginBlockedAction,
        _ => LoginFailedAction
    };

    // Timestamps are stored in UTC but read back without a kind. Marking them makes the
    // JSON carry a "Z", so the browser does not mistake them for local time.
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
