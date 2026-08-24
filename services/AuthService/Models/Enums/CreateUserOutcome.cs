namespace AuthService.Models.Enums;

public enum CreateUserOutcome
{
    Success,
    DuplicateUsername,
    PasswordTooShort,
    RoomNumberRequiredForDoctor
}
