using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Services;

public sealed class SessionValidationService(AuthDbContext context, TimeProvider clock)
{
    public async Task<bool> ValidateAsync(SessionValidationRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        DateTime expiresAt = DateTime.MinValue;
        try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(request.ExpiresAtUnixSeconds).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return false; }
        if (request.UserId == Guid.Empty || request.SessionVersion == Guid.Empty || string.IsNullOrWhiteSpace(request.TokenId)
            || request.TokenId.Length > 128 || expiresAt <= now) return false;

        // Indexed expiry cleanup is bounded per request, including requests handled by another replica.
        var expired = await context.RevokedSessions.Where(session => session.ExpiresAtUtc <= now)
            .OrderBy(session => session.ExpiresAtUtc).Take(500).ToListAsync(cancellationToken);
        if (expired.Count > 0)
        {
            context.RevokedSessions.RemoveRange(expired);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another replica may have removed the same expired rows.
                context.ChangeTracker.Clear();
            }
        }

        var active = await context.Users.AsNoTracking().AnyAsync(user => user.Id == request.UserId
            && user.IsActive && !user.IsDeleted && user.SessionVersion == request.SessionVersion, cancellationToken);
        if (!active || await context.RevokedSessions.AnyAsync(session => session.TokenId == request.TokenId, cancellationToken))
            return false;
        if (!request.Revoke) return true;

        context.RevokedSessions.Add(new RevokedSession { TokenId = request.TokenId, ExpiresAtUtc = expiresAt });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            if (await context.RevokedSessions.AsNoTracking().AnyAsync(session => session.TokenId == request.TokenId, cancellationToken))
                return false; // A competing logout has already committed this revocation.
            throw;
        }
    }
}
