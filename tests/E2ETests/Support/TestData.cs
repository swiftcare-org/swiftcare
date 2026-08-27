namespace E2ETests.Support;

// Every value a test creates (username, NIC, phone, ...) carries a per-run stamp
// plus an incrementing counter, so the rows are identifiable as test data and
// never collide - the suite is safe to re-run against a database that is not
// reset between runs. See tests/E2ETests/README.md.
public static class TestData
{
    public static readonly string RunId = DateTime.UtcNow.ToString("MMddHHmmss");

    private static int _counter;

    private static int Next() => Interlocked.Increment(ref _counter);

    public static string Username(string prefix) => $"e2e.{prefix}.{RunId}.{Next()}";

    // Comfortably above AuthService's 8-character minimum.
    public static string Password() => $"E2e!pass{RunId}{Next()}";

    public static string FullName(string label) => $"E2E {label} {RunId}-{Next()}";

    public static string RoomNumber() => $"R{RunId[^3..]}{Next()}";

    // 12 digits: matches the numeric branch of the NIC pattern (^[0-9]{12}$).
    public static string Nic()
    {
        var value = (DateTime.UtcNow.Ticks + Next()) % 1_000_000_000_000L;
        return value.ToString("D12");
    }

    // 0 followed by 9 digits: matches the local Sri Lankan phone branch (^0[0-9]{9}$).
    public static string Phone()
    {
        var value = (DateTime.UtcNow.Ticks + Next()) % 1_000_000_000L;
        return "0" + value.ToString("D9");
    }
}
