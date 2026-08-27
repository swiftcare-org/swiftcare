using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using E2ETests.Config;

namespace E2ETests.Support;

// Sets up test preconditions by talking straight to the API Gateway over HTTP -
// the same entry point the frontend uses - instead of clicking through the
// browser. This keeps each test's Selenium steps focused on the one behaviour
// under test. Request/response shapes mirror frontend/src/api/*.ts.
//
// Synchronous wrappers are exposed so test bodies stay in the same imperative
// style as the Selenium calls; there is no synchronization context under xUnit,
// so blocking on the tasks is safe.
public sealed class SeedClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public SeedClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(TestConfig.GatewayUrl) };
    }

    public SeededPatient RegisterPatient(string? fullName = null) =>
        RegisterPatientAsync(fullName).GetAwaiter().GetResult();

    public string AddAllergy(string patientId, string allergyName, string severity, string? notes = null) =>
        AddAllergyAsync(patientId, allergyName, severity, notes).GetAwaiter().GetResult();

    public SeededUser CreateUser(string role, string? username = null, string? password = null) =>
        CreateUserAsync(role, username, password).GetAwaiter().GetResult();

    private async Task<SeededPatient> RegisterPatientAsync(string? fullName)
    {
        var name = fullName ?? TestData.FullName("Patient");
        var nic = TestData.Nic();
        var phone = TestData.Phone();
        const string bloodGroup = "O+";

        var request = new
        {
            nic,
            fullName = name,
            dateOfBirth = "1990-05-15",
            gender = "Male",
            address = "1 Seed Lane, Colombo",
            phoneNumber = phone,
            bloodGroup,
        };

        var token = await TokenForAsync("reception.silva");
        using var response = await SendAsync(HttpMethod.Post, "/api/patients", request, token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RegisteredPatientBody>(Json)
                   ?? throw new InvalidOperationException("Empty response registering a seed patient.");
        return new SeededPatient(body.PatientId, nic, name, phone, bloodGroup);
    }

    private async Task<string> AddAllergyAsync(string patientId, string allergyName, string severity, string? notes)
    {
        var request = new { allergyName, severity, notes };

        var token = await TokenForAsync("dr.chen");
        using var response = await SendAsync(HttpMethod.Post, $"/api/patients/{patientId}/allergies", request, token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AllergyBody>(Json)
                   ?? throw new InvalidOperationException("Empty response adding a seed allergy.");
        return body.AllergyId;
    }

    private async Task<SeededUser> CreateUserAsync(string role, string? username, string? password)
    {
        var user = username ?? TestData.Username(role.ToLowerInvariant());
        var pw = password ?? TestData.Password();

        var request = new
        {
            username = user,
            password = pw,
            fullName = TestData.FullName(role),
            role,
            roomNumber = role == "Doctor" ? TestData.RoomNumber() : null,
        };

        var token = await TokenForAsync("admin.fernando");
        using var response = await SendAsync(HttpMethod.Post, "/api/users", request, token);
        response.EnsureSuccessStatusCode();
        return new SeededUser(user, pw, role);
    }

    private async Task<string> TokenForAsync(string username)
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/auth/login", new { username, password = TestConfig.SeedPassword }, Json);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>(Json)
                   ?? throw new InvalidOperationException($"Empty login response for seed account '{username}'.");
        return body.Token;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object body, string token)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _http.SendAsync(request);
    }

    public void Dispose() => _http.Dispose();

    private sealed record LoginBody(string Token);

    private sealed record RegisteredPatientBody(string PatientId);

    private sealed record AllergyBody(string AllergyId);
}

public sealed record SeededPatient(string PatientId, string Nic, string FullName, string PhoneNumber, string BloodGroup);

public sealed record SeededUser(string Username, string Password, string Role);
