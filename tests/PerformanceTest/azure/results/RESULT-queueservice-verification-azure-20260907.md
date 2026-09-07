# Performance run report - QUEUESERVICE VERIFICATION (Azure) - DEFERRED

**Jira:** SWC-67  **Date:** 2026-09-07  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 6.2

This is a second, network-inclusive data point alongside the existing local SWC-67 results, not
a replacement. This particular check is deferred; see below.

## Status

Deferred. None of the plan's QueueService checks were run this round.

## What was planned (TEST-PLAN-AZURE.md section 6.2)

| Check | Pass criterion |
|---|---|
| Queue entries created for every registration | Row count in the queue table equals successful `POST /api/patients` during the run window |
| No duplicate queue entries | Zero duplicate `(PatientId, QueueDate)` pairs |
| Sequential numbering held under concurrency | No gaps or duplicate `QueueNumber` values within the run window |
| Consumer lag | Kafka consumer group lag returns to near-zero shortly after the Load run ends |

## Why it could not run

QueueService (SWC-19 / SWC-71) is deployed to Azure: CD builds the `swiftcare-queue` image
tagged with the deploy commit, runs `--migrate` as a finite Container Apps job, and runs the
service as a private background Kafka consumer. It is consuming `patient-checked-in` events into
`swiftcare_queue` right now. But it exposes no surface the load-generator machine can observe:

- **No read API.** SWC-19 is deliberately consume-only; the service exposes only `/health`. No
  HTTP endpoint returns queue rows, counts, or numbers.
- **Database is VNet-private.** Azure MySQL Flexible Server has
  `network.publicNetworkAccess = Disabled` (CD asserts this and fails otherwise), a delegated
  subnet and a private DNS zone. `swiftcare_queue` is reachable only from inside the Azure
  VNet. There are no firewall allow-rules.
- **Kafka is VNet-private.** The broker is an Azure Container Instances group with
  `ip_address_type = Private`. There is no Kafka UI on Azure (only in local `docker-compose`),
  and no Application Insights / OpenTelemetry in any service, so consumer-group lag is not
  exposed to any external tool.

## What running it later requires

Any one of the following, all needing access that DevOps controls:

1. A temporary MySQL firewall rule for the test machine's IP plus the `QUEUE_DB_PASSWORD`
   secret, then run the four checks as SQL against `swiftcare_queue` (`COUNT(*)` in the run
   window; `GROUP BY PatientId, QueueDate HAVING COUNT(*) > 1`; a `QueueNumber` gap and
   duplicate scan per `QueueDate`).
2. `az containerapp exec` into an in-VNet container to run the same SQL, plus
   `kafka-consumer-groups` against the private broker, describing the `queue-service` consumer
   group, for lag.
3. A `swiftcare-logs` Log Analytics query. QueueService logs each created entry, each rejected
   duplicate, and each allocation attempt; lag can be inferred from the processing-timestamp
   trail relative to the Load run window.

## Note on comparability

The local SWC-67 baseline contains no QueueService data; the service did not exist when those
runs were made (`../../local/README.md`: "QueueService (the consumer) is not built yet, so
events accumulate in the topic"). So this verification is net-new work, not a local/Azure
comparison, and is best done as its own exercise once in-VNet access exists.

## Follow-up

1. DevOps to provide one of the access paths above.
2. Re-run against the queue rows produced by the Load run in `RESULT-load-azure-20260907.md`,
   before other traffic changes the table.
