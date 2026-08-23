namespace ApiGateway.Models;

// Matches the shape AuthService's MessageResponse already returns for its own 401s,
// so every 401 in the login/logout flow - whether it originates at the Gateway or at
// AuthService - has the same error contract.
public sealed record MessageResponse(string Message);
