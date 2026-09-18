namespace E2ETests.Support;

public static class E2ETestCollections
{
    public const string SharedQueue = "Shared queue E2E";
}

// These workflows select from or observe the clinic-wide queue. QueueService has no
// test-run/tenant partition, so a Call Next in one test could otherwise consume the
// patient another test is asserting. xUnit guarantees that an opted-out collection
// never overlaps any other collection, while independent browser tests still use the
// bounded parallel worker pool configured in xunit.runner.json.
[CollectionDefinition(E2ETestCollections.SharedQueue, DisableParallelization = true)]
public sealed class SharedQueueE2ECollectionDefinition;
