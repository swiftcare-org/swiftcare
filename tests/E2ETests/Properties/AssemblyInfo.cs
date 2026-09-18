using Xunit;

// The browser tests exercise one shared application stack and queue database.
// Running test classes concurrently lets one workflow change queue state while
// another is waiting for a real-time update, and can also start enough Chrome
// sessions at once for ChromeDriver creation to time out. Keep this E2E assembly
// sequential; unit-test projects remain free to run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
