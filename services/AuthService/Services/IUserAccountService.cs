using AuthService.Models.Dtos;

namespace AuthService.Services;

public interface IUserAccountService
{
    Task<CreateUserResult> CreateUserAsync(
        CreateUserRequest request,
        string correlationId,
        Guid actingAdminId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(CancellationToken cancellationToken = default);
}
