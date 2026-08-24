using AuthService.Data;
using AuthService.Models.Configuration;
using AuthService.Models.Dtos;
using AuthService.Models.Entities;
using AuthService.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Services;

public sealed class UserAccountService : IUserAccountService
{
    private readonly AuthDbContext _dbContext;
    private readonly ILogger<UserAccountService> _logger;

    public UserAccountService(AuthDbContext dbContext, ILogger<UserAccountService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CreateUserResult> CreateUserAsync(
        CreateUserRequest request,
        string correlationId,
        Guid actingAdminId,
        CancellationToken cancellationToken = default)
    {
        if (request.Password.Length < PasswordPolicy.MinimumLength)
        {
            LogRejection(CreateUserOutcome.PasswordTooShort, actingAdminId);
            return new CreateUserResult { Outcome = CreateUserOutcome.PasswordTooShort };
        }

        if (request.Role == UserRole.Doctor && string.IsNullOrWhiteSpace(request.RoomNumber))
        {
            LogRejection(CreateUserOutcome.RoomNumberRequiredForDoctor, actingAdminId);
            return new CreateUserResult { Outcome = CreateUserOutcome.RoomNumberRequiredForDoctor };
        }

        // Deliberately not filtered by !IsDeleted: the unique index on Username has no
        // filter, so a collision with a soft-deleted row must still be caught here rather
        // than surfacing as a DbUpdateException at SaveChangesAsync. This differs from
        // AuthenticationService.LoginAsync, which does filter !IsDeleted, because a
        // soft-deleted account should be unreachable for sign-in but still occupies its
        // username permanently.
        var usernameExists = await _dbContext.Users
            .AnyAsync(u => u.Username == request.Username, cancellationToken);

        if (usernameExists)
        {
            LogRejection(CreateUserOutcome.DuplicateUsername, actingAdminId);
            return new CreateUserResult { Outcome = CreateUserOutcome.DuplicateUsername };
        }

        var isDoctor = request.Role == UserRole.Doctor;
        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName,
            Role = request.Role!.Value,
            RoomNumber = isDoctor ? request.RoomNumber!.Trim() : null,
            IsActive = true
        };

        _dbContext.Users.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two admins submitting the same username concurrently can both pass the
            // AnyAsync check above; the unique index is the final backstop.
            LogRejection(CreateUserOutcome.DuplicateUsername, actingAdminId);
            return new CreateUserResult { Outcome = CreateUserOutcome.DuplicateUsername };
        }

        _logger.LogInformation(
            "User account created: createdUserId={CreatedUserId} role={Role} by adminUserId={AdminUserId}",
            user.Id,
            user.Role,
            actingAdminId);

        return new CreateUserResult
        {
            Outcome = CreateUserOutcome.Success,
            User = ToSummary(user)
        };
    }

    public async Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .Select(u => new UserSummaryResponse
            {
                UserId = u.Id,
                Username = u.Username,
                FullName = u.FullName,
                Role = u.Role.ToString(),
                RoomNumber = u.RoomNumber,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    private static UserSummaryResponse ToSummary(User user) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        Role = user.Role.ToString(),
        RoomNumber = user.RoomNumber,
        IsActive = user.IsActive,
        CreatedAt = user.CreatedAt
    };

    private void LogRejection(CreateUserOutcome outcome, Guid actingAdminId)
    {
        _logger.LogInformation(
            "User creation rejected: outcome={Outcome} by adminUserId={AdminUserId}",
            outcome,
            actingAdminId);
    }
}
