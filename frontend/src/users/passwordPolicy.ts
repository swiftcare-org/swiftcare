// Mirrors AuthService's PasswordPolicy.MinimumLength. The frontend has no way to import a
// C# constant, so this must be kept in sync by hand - the server remains authoritative
// regardless of what a pre-submit check catches.
export const MINIMUM_PASSWORD_LENGTH = 8;

export const PASSWORD_TOO_SHORT_MESSAGE = `Password must be at least ${MINIMUM_PASSWORD_LENGTH} characters.`;
