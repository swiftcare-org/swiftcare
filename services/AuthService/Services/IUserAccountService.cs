using AuthService.Models.Dtos;

namespace AuthService.Services;

public interface IUserAccountService
{
    Task<CreateUserResult> CreateUserAsync(
        CreateUserRequest request,
        AdminActionContext context,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserSummaryResponse>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DoctorSummaryResponse>> GetActiveDoctorsAsync(CancellationToken cancellationToken = default);

    Task<UserActionResult> UpdateUserAsync(
        Guid userId,
        UpdateUserRequest request,
        AdminActionContext context,
        CancellationToken cancellationToken = default);

    Task<UserActionResult> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        AdminActionContext context,
        CancellationToken cancellationToken = default);

    Task<UserActionResult> DeactivateUserAsync(
        Guid userId,
        AdminActionContext context,
        CancellationToken cancellationToken = default);

    Task<UserActionResult> ReactivateUserAsync(
        Guid userId,
        AdminActionContext context,
        CancellationToken cancellationToken = default);
}
