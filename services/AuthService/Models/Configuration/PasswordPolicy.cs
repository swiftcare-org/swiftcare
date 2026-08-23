namespace AuthService.Models.Configuration;

// FR-AS-005: the same minimum applies to account creation, password reset, and the
// bootstrap command. Kept here so those paths cannot drift apart.
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
}
