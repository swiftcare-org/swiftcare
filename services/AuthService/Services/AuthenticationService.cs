using AuthService.Data;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    // A real BCrypt hash with no known plaintext, used to verify against when the
    // username doesn't resolve to a user. This keeps the work done - and therefore
    // the response latency - the same whether or not the account exists.
    private const string DummyPasswordHash = "$2a$11$CwTycUXWue0Thq9StjUM0uJ8L9L4gCFTFmZ0fWqe9zHnkV0F.k9jS";

    private readonly AuthDbContext _dbContext;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        AuthDbContext dbContext,
        IJwtTokenService jwtTokenService,
        ILogger<AuthenticationService> logger)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(
        string username,
        string password,
        string correlationId,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users
            .Where(u => u.Username == username && !u.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        // Always run a BCrypt verify, even when the user doesn't exist, so that
        // response timing never reveals whether a username is registered.
        var passwordIsValid = BCrypt.Net.BCrypt.Verify(password, user?.PasswordHash ?? DummyPasswordHash);

        if (user is null || !passwordIsValid)
        {
            await WriteAuditEntryAsync(user?.Id, LoginOutcome.InvalidCredentials, correlationId, ipAddress, cancellationToken);
            _logger.LogInformation(
                "Login attempt failed: outcome={Outcome} userId={UserId}",
                LoginOutcome.InvalidCredentials,
                user?.Id);

            return new LoginResult { Outcome = LoginOutcome.InvalidCredentials };
        }

        // Credentials are validated before the activation check so that an attacker
        // cannot use a valid username alone to discover whether an account exists.
        if (!user.IsActive)
        {
            await WriteAuditEntryAsync(user.Id, LoginOutcome.AccountDeactivated, correlationId, ipAddress, cancellationToken);
            _logger.LogInformation(
                "Login attempt failed: outcome={Outcome} userId={UserId}",
                LoginOutcome.AccountDeactivated,
                user.Id);

            return new LoginResult { Outcome = LoginOutcome.AccountDeactivated };
        }

        var (token, expiresAt) = _jwtTokenService.GenerateToken(user);

        await WriteAuditEntryAsync(user.Id, LoginOutcome.Success, correlationId, ipAddress, cancellationToken);
        _logger.LogInformation(
            "Login attempt succeeded: outcome={Outcome} userId={UserId}",
            LoginOutcome.Success,
            user.Id);

        return new LoginResult
        {
            Outcome = LoginOutcome.Success,
            Token = token,
            ExpiresAt = expiresAt,
            User = new AuthenticatedUser
            {
                UserId = user.Id,
                FullName = user.FullName,
                Role = user.Role,
                RoomNumber = user.Role == UserRole.Doctor ? user.RoomNumber : null
            }
        };
    }

    private async Task WriteAuditEntryAsync(
        Guid? userId,
        LoginOutcome outcome,
        string correlationId,
        string ipAddress,
        CancellationToken cancellationToken)
    {
        _dbContext.LoginAuditEntries.Add(new LoginAuditEntry
        {
            UserId = userId,
            Outcome = outcome,
            CorrelationId = correlationId,
            IpAddress = ipAddress,
            OccurredAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
