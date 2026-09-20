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
    private readonly HashSet<string> _registeredPatientIds = [];

    public SeedClient()
    {
        _http = new HttpClient { BaseAddress = new Uri(TestConfig.GatewayUrl) };
    }

    public SeededPatient RegisterPatient(string? fullName = null) =>
        RegisterPatientAsync(fullName).GetAwaiter().GetResult();

    // Most profile/search tests need a patient record, not a queue entry. Registration
    // necessarily publishes patient-checked-in, so wait for that asynchronous side effect
    // and remove only this test's row before the browser steps begin.
    public SeededPatient RegisterPatientOutsideQueue(string? fullName = null)
    {
        var patient = RegisterPatient(fullName);
        RemovePatientFromTodayQueue(patient.PatientId);
        _registeredPatientIds.Remove(patient.PatientId);
        return patient;
    }

    // UI registration tests do not receive the new patient id from their page object.
    // Resolve the unique generated name through the same Gateway search endpoint, then
    // remove the registration-created queue entry without touching another test's data.
    public SeededPatient FindPatientByName(string fullName) =>
        FindPatientByNameAsync(fullName).GetAwaiter().GetResult();

    public void RemovePatientFromTodayQueue(string patientId)
    {
        WaitUntilWaiting(patientId);
        if (!QueueDatabase.DeleteTodayQueueEntry(patientId))
        {
            throw new InvalidOperationException(
                $"Could not remove today's queue entry for E2E patient {patientId}.");
        }
        _registeredPatientIds.Remove(patientId);
    }

    public string AddAllergy(string patientId, string allergyName, string severity, string? notes = null) =>
        AddAllergyAsync(patientId, allergyName, severity, notes).GetAwaiter().GetResult();

    // dateDiagnosed defaults to UTC-today: PatientService rejects a date after the
    // Asia/Colombo clinic-local calendar date (see ClinicDateProvider), and UTC is never
    // ahead of Colombo (UTC+5:30, no DST), so UTC-today is always a safe, never-future value.
    public string AddChronicCondition(
        string patientId, string conditionName, string? dateDiagnosed = null, string? notes = null) =>
        AddChronicConditionAsync(patientId, conditionName, dateDiagnosed, notes).GetAwaiter().GetResult();

    public SeededUser CreateUser(string role, string? username = null, string? password = null) =>
        CreateUserAsync(role, username, password).GetAwaiter().GetResult();

    // Waits for the patient-checked-in Kafka consumer to place a registered patient
    // into the WAITING pool. Needed before CallNext, which acts on whichever entry is
    // first in line - without this wait, a call made immediately after RegisterPatient
    // can race the consumer and act on a leftover entry from a previous run instead.
    public void WaitUntilWaiting(string patientId, TimeSpan? timeout = null) =>
        WaitUntilWaitingAsync(patientId, timeout ?? TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();

    // Call Next selects the clinic-wide first waiting entry. Fail before calling if an
    // older entry would be taken instead of this test's patient; never consume another
    // person's queue entry just to satisfy a browser-test precondition.
    public void EnsureNextWaitingPatientIs(string patientId) =>
        EnsureNextWaitingPatientIsAsync(patientId).GetAwaiter().GetResult();

    // Calls next as the given account (any active Doctor, seeded or created via
    // CreateUser) and returns the room/queue-number pair QueueService assigned.
    public CalledQueueEntry CallNext(string username, string password) =>
        CallNextAsync(username, password).GetAwaiter().GetResult();

    public CurrentQueueAssignment GetCurrentForDoctor(string username, string password) =>
        GetCurrentForDoctorAsync(username, password).GetAwaiter().GetResult();

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
        var patient = new SeededPatient(body.PatientId, nic, name, phone, bloodGroup);
        _registeredPatientIds.Add(patient.PatientId);
        return patient;
    }

    private async Task<SeededPatient> FindPatientByNameAsync(string fullName)
    {
        var token = await TokenForAsync("reception.silva");
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/patients/search?q={Uri.EscapeDataString(fullName)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var matches = await response.Content.ReadFromJsonAsync<List<SearchPatientBody>>(Json) ?? [];
        var match = matches.SingleOrDefault(patient => patient.FullName == fullName)
            ?? throw new InvalidOperationException(
                $"Could not resolve the E2E patient registered as '{fullName}'.");

        return new SeededPatient(
            match.PatientId,
            match.Nic,
            match.FullName,
            match.PhoneNumber,
            match.BloodGroup);
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

    private async Task<string> AddChronicConditionAsync(
        string patientId, string conditionName, string? dateDiagnosed, string? notes)
    {
        var request = new
        {
            conditionName,
            dateDiagnosed = dateDiagnosed ?? DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            notes,
        };

        var token = await TokenForAsync("reception.silva");
        using var response = await SendAsync(HttpMethod.Post, $"/api/patients/{patientId}/conditions", request, token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChronicConditionBody>(Json)
                   ?? throw new InvalidOperationException("Empty response adding a seed chronic condition.");
        return body.ConditionId;
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
        var createdUser = await response.Content.ReadFromJsonAsync<CreatedUserBody>(Json)
            ?? throw new InvalidOperationException($"Empty response creating E2E user '{user}'.");
        return new SeededUser(user, pw, role, request.roomNumber, createdUser.UserId, request.fullName);
    }

    private async Task WaitUntilWaitingAsync(string patientId, TimeSpan timeout)
    {
        var token = await TokenForAsync("dr.chen");
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/queue/today/waiting");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var entries = await response.Content.ReadFromJsonAsync<List<WaitingEntryBody>>(Json) ?? [];
            if (entries.Any(entry => entry.PatientId == patientId))
            {
                return;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Patient {patientId} did not appear in the waiting pool within {timeout.TotalSeconds}s. " +
                    "The patient-checked-in Kafka consumer may be down - see QueueService/README.md.");
            }
            await Task.Delay(500);
        }
    }

    private async Task EnsureNextWaitingPatientIsAsync(string patientId)
    {
        var token = await TokenForAsync("dr.chen");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/queue/today/waiting");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var entries = await response.Content.ReadFromJsonAsync<List<WaitingEntryBody>>(Json) ?? [];
        if (entries.FirstOrDefault()?.PatientId != patientId)
        {
            throw new InvalidOperationException(
                $"E2E patient {patientId} is not first in the shared waiting queue. " +
                "Run queue-dependent tests against an isolated queue; no other patient's entry was called.");
        }
    }

    private async Task<CalledQueueEntry> CallNextAsync(string username, string password)
    {
        var token = await TokenForAsync(username, password);
        using var response = await SendAsync(HttpMethod.Put, "/api/queue/call-next", new { }, token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CalledQueueEntryBody>(Json)
                   ?? throw new InvalidOperationException("Empty response calling next.");
        return new CalledQueueEntry(body.QueueNumber, body.RoomNumber);
    }

    private async Task<CurrentQueueAssignment> GetCurrentForDoctorAsync(string username, string password)
    {
        var token = await TokenForAsync(username, password);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/queue/today/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Doctor '{username}' has no current queue assignment.");
        }

        return await response.Content.ReadFromJsonAsync<CurrentQueueAssignment>(Json)
            ?? throw new InvalidOperationException($"Empty current-assignment response for doctor '{username}'.");
    }

    // password is null for the dev-seeded accounts (dr.chen, reception.silva,
    // admin.fernando), which all share TestConfig.SeedPassword; a throwaway account
    // created via CreateUser carries its own generated password instead.
    private async Task<string> TokenForAsync(string username, string? password = null)
    {
        using var response = await _http.PostAsJsonAsync(
            "/api/auth/login", new { username, password = password ?? TestConfig.SeedPassword }, Json);
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

    public void Dispose()
    {
        try
        {
            foreach (var patientId in _registeredPatientIds)
            {
                QueueDatabase.DeleteTodayQueueEntry(patientId);
            }
        }
        finally
        {
            _http.Dispose();
        }
    }

    private sealed record LoginBody(string Token);

    private sealed record RegisteredPatientBody(string PatientId);

    private sealed record CreatedUserBody(string UserId);

    private sealed record SearchPatientBody(
        string PatientId,
        string FullName,
        string Nic,
        string PhoneNumber,
        string BloodGroup);

    private sealed record AllergyBody(string AllergyId);

    private sealed record ChronicConditionBody(string ConditionId);

    private sealed record WaitingEntryBody(string PatientId);

    private sealed record CalledQueueEntryBody(string QueueNumber, string RoomNumber);
}

public sealed record SeededPatient(string PatientId, string Nic, string FullName, string PhoneNumber, string BloodGroup);

public sealed record SeededUser(
    string Username,
    string Password,
    string Role,
    string? RoomNumber,
    string UserId,
    string FullName);

public sealed record CalledQueueEntry(string QueueNumber, string RoomNumber);

public sealed record CurrentQueueAssignment(
    string PatientId,
    string QueueNumber,
    string Status,
    string DoctorId,
    string DoctorName,
    string RoomNumber);
