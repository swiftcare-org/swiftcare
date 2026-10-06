using AuthService.Models.Dtos;

namespace AuthService.Services;

public interface IUserAccountService
{
    Task<CreateUserResult> CreateUserAsync(
        CreateUserRequest request,
        AdminActionContext context,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(CancellationToken cancellationToken = default);
}
