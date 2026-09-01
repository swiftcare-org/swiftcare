namespace E2ETests.Config;

// Fails fast on missing config, same convention as frontend/src/api/client.ts:
// a misconfigured environment should error immediately, not fail confusingly
// mid-test.
public static class TestConfig
{
    public static string BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5173";

    // The API Gateway, used by SeedClient to set up preconditions over HTTP
    // (register a patient, add an allergy, create a user) without driving the
    // browser. Matches the frontend's VITE_GATEWAY_URL default.
    public static string GatewayUrl => Environment.GetEnvironmentVariable("E2E_GATEWAY_URL") ?? "http://localhost:8000";

    public static string SeedPassword =>
        Environment.GetEnvironmentVariable("AUTH_SEED_PASSWORD")
        ?? throw new InvalidOperationException(
            "AUTH_SEED_PASSWORD is not set. It must match the value used to seed the AuthService " +
            "development database (see .env) so the E2E suite can log in as the seeded users.");

    public static bool Headless => Environment.GetEnvironmentVariable("E2E_HEADLESS") != "false";
}
