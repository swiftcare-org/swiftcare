# Performance run report - STRESS (SWC-126 Sprint 3 clinical flow, combined)

## Headline

**Breaking point at about 46-62 concurrent users (roughly 23-31 doctors and as many counter
sessions), at a peak of 52 req/s, about 60-90 s into the ramp.** **QueueService CPU saturated
first**, pinned at its 1.0 CPU cap from t=55 s for the rest of the run. Two causes, both in
Sprint 2 QueueService endpoints:

- the counter's `GET /api/queue/today` returns the whole day's queue on every poll (1.46 MB for
  a 6,000-entry day);
- concurrent `PUT /api/queue/call-next` transactions **deadlock in MySQL and return 500**. That
  happened 1,842 times, from 14 active threads onward.

**The Sprint 3 endpoints did not break.** They returned no error responses across 10,027 samples
(one pending-list sample breached the 1.5 s Duration Assertion at 1.8 s), and their per-minute
p95 never passed 1.6 s.

| Profile | Value |
|---|---|
| Threads | 150 Doctor + 150 Receptionist = 300, linear ramp over the whole 600 s |
| Stack | CPU/memory capped (`docker-compose.perf.yml`) |
| Samples / throughput | 23,251 samples, 38 req/s average, 52 req/s peak |
| Errors | 1,872 (8.05%): 1,842 call-next 500s, 29 call-next 409s, 1 Duration Assertion |

## Run metadata

| Field | Value |
|---|---|
| Run type | Stress (find the knee), combined doctor and counter traffic |
| Jira | SWC-126 |
| Date / time | 2026-09-27 17:29:19 to 17:39:30 IST |
| JMeter version | 5.6.3, CLI (non-GUI), on Java 21.0.12 |
| Command | `jmeter -n -t SWC-126-clinical-flow.jmx -q user.properties -JusersDoctor=150 -JusersCounter=150 -JrampUp=600 -Jduration=600 -JstepDelay=100 -JstepRange=200 -JcycleDelay=5000 -JcycleRange=5000 -JpollDelay=300 -JpollRange=300 -JdispenseAfterMs=10000 -l results/stress-SWC-126.jtl -e -o results/stress-SWC-126-report` |
| Pacing | Doctor: 100-300 ms between steps, 5-10 s before the next patient. Counter: queue and pending list every 300-600 ms, dispense 10 s or more after a save. |
| Resource limits | `docker-compose.perf.yml`: mysql 2.0 CPU / 1 GB; authservice, patientservice, queueservice, medicalrecordservice, prescriptionservice and apigateway 1.0 CPU / 512 MB each |
| Machine | Windows 11, Docker Desktop (7.44 GiB to the Docker VM), JMeter on the same machine |
| Data | Fresh stack (`docker compose down -v`, migrations applied), then `seed-clinical-flow.ps1 -DoctorCount 150 -ReceptionistCount 150 -QueueVolume 6000`: 6,000 today-dated Waiting entries at the start |
| Calibration | `TEST-PLAN.md` section 9.6: ×15 the Load run's 20 users, which left every container under 11% CPU |
| Report | `results/stress-SWC-126-report/index.html` |

## Time series (30 s windows, by sample end time)

| t (s) | Active threads | Throughput (req/s) | p50 (ms) | p95 (ms) | Error % |
|---:|---:|---:|---:|---:|---:|
| 0 | 16 | 20.6 | 32 | 247 | 0.32 |
| 30 | 32 | 42.7 | 26 | 611 | 0.39 |
| **60** | **46** | **51.8 ← peak** | 30 | 1,147 | 0.84 |
| **90** | **62** | 48.1 | 43 | 1,909 | **2.84** |
| **120** | **76** | 48.0 | 62 | **2,729** | 4.03 |
| 150 | 92 | 45.9 | 174 | 3,123 | 6.39 |
| 180 | 106 | 36.9 | 492 | 5,284 | 9.85 |
| 240 | 136 | 38.4 | 1,027 | 7,594 | 8.16 |
| 300 | 166 | 34.5 | 2,258 | 11,309 | 12.37 |
| 360 | 196 | 36.6 | 2,498 | 12,521 | 9.20 |
| 420 | 226 | 33.4 | 4,020 | 13,529 | 10.68 |
| 480 | 256 | 39.5 | 4,464 | 11,597 | 9.29 |
| 540 | 286 | 41.8 | 6,014 | 14,326 | 10.85 |
| 570 | 298 | 36.8 | 6,524 | 14,923 | 10.05 |

The final partial window (threads stopping at 600 s) is left out.

## Breaking point - against TEST-PLAN.md section 9.5

| Trigger | Fired? | Where |
|---|---|---|
| Throughput flat or falling while threads still rise | **YES, first** | Peaks at 51.8 req/s at about 46 threads (t=60-90 s), then stays at 33-48 req/s for the remaining 500 s while threads rise to 300 |
| Aggregate error rate above 1% | **YES** | From about 62 threads (t=90 s), sustained to the end: 2.8-14.5% per window |
| Aggregate p95 above 2000 ms | **YES** | From about 76 threads (t=120 s), sustained to the end, reaching 15 s |

**Breaking point: about 46-62 concurrent users at about 50 req/s, t=60-90 s into the ramp.**
Beyond it, latency and errors grow while throughput stays flat. This is the same queueing
pattern as Sprint 1 and SWC-87, but with call-next failing outright rather than only slowing
down.

## Per endpoint (whole run)

| Class | Label | Samples | Errors | p50 | p95 | max (ms) |
|---|---|---:|---:|---:|---:|---:|
| Read | GET /api/prescriptions/pending | 6,810 | 1 (duration) | 82 | 403 | 1,820 |
| Read | GET /api/consultations/patient/{id}/latest-follow-up | 325 | 0 | 10 | 151 | 897 |
| Read | GET /api/consultations/latest-completed | 324 | 0 | 9 | 92 | 323 |
| Read | GET /api/prescriptions/patient/{id} | 324 | 0 | 12 | 83 | 344 |
| Read | GET /api/prescriptions/queue/{queueId} | 311 | 0 | 62 | 334 | 883 |
| Write | POST /api/consultations/{id}/vitals | 325 | 0 | 22 | 411 | 1,726 |
| Write | POST /api/consultations/{id}/complete | 325 | 0 | 34 | 445 | 2,426 |
| Write | POST /api/prescriptions | 324 | 0 | 32 | 434 | 1,491 |
| Write | POST /api/prescriptions/{id}/items | 324 | 0 | 28 | 322 | 992 |
| Write | DELETE /api/prescriptions/{id}/items/{medicineId} | 324 | 0 | 28 | 254 | 1,440 |
| Write | PUT /api/prescriptions/{id}/dispense | 311 | 0 | 151 | 1,183 | 2,328 |
| Sprint 2 | GET /api/queue/today | 6,955 | 0 | **5,752** | **13,073** | 17,528 |
| Sprint 2 | PUT /api/queue/call-next | 2,199 | **1,871** | **9,029** | **15,329** | 19,640 |
| Sprint 2 | GET /api/queue/today/current | 3,445 | 0 | 2,640 | 7,133 | 10,304 |
| Sprint 2 | POST /api/consultations | 325 | 0 | 25 | 599 | 2,181 |

Sprint 3 per minute: reads p95 27 → 537 ms at worst (minute 4); writes p95 61 → 1,549 ms at
worst (minute 4), then 715-933 ms.

## First resource to saturate - measured

`docker stats`, sampled every ~30 s (`stress-SWC-126-docker-stats.log`):

| Container | CPU cap | CPU under load | Notes |
|---|---:|---|---|
| **queueservice** | 1.0 (100%) | **96-103% from 17:30:12 (t=55 s, about 30 threads) to the end** | First to saturate, pinned for about 540 s |
| mysql | 2.0 (200%) | 20-203%, reaching the cap intermittently from 17:32:21 (t=185 s) | Second. Serializable call-next transactions and repeated full-day queue reads |
| prescriptionservice | 1.0 | 1-42%, one 98% sample at t=55 s | Headroom otherwise |
| apigateway | 1.0 | 1-32% | Headroom |
| authservice | 1.0 | up to 51% during the login ramp, then about 0% | Once-per-thread logins |
| medicalrecordservice | 1.0 | 0-9% | Idle |
| patientservice | 1.0 | about 0% | Not exercised |

Memory stayed well under every cap: queueservice ended at 217 MiB, mysql at 639 MiB.

**The bottleneck is QueueService CPU**, reached at about 30 active threads. The throughput
plateau shows up in the JMeter series about 30 s later. Two things feed it:

1. **Full-day queue payload.** `GET /api/queue/today` returned about 1.46 MB (6,000 entries) on
   every counter poll. With 150 counters polling every 300-600 ms, that is 6,955 full-table
   serialisations. Its median rose to 5.8 s. This confirms the SWC-87 follow-up: a
   production-scale day lowers QueueService's knee. SWC-87 saturated at about 334 req/s with 219
   rows; with 6,000 rows it saturates at about 50 req/s.
2. **Call-next deadlocks.** `CallNextPatientService` runs a Serializable transaction that checks
   the doctor and room, then picks the first Waiting entry for today. `QueueEntries` has no index
   on `(QueueDate, Status)`, so the read locks a wide range, and concurrent calls deadlock:

   ```
   fail: Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware[1]
         An unhandled exception has occurred while executing the request.
    ---> Microsoft.EntityFrameworkCore.DbUpdateException ...
    ---> MySqlConnector.MySqlException (0x80004005): Deadlock found when trying to get lock; try restarting transaction
   ```

   The exception is not caught or retried, so the doctor gets a 500. The first 500 came at
   t=24 s with only 14 active threads (about 7 doctors), before QueueService's CPU saturated.
   By minute 3, 90% of call-next requests failed (211 of 235). **This is a concurrency defect,
   not only a capacity limit.** It was invisible in Load because 10 doctors pacing 40-50 s
   between patients rarely call at the same moment.

## Asynchronous completion under Stress

| Signal | Load (for comparison) | Stress |
|---|---|---|
| Queue entry Completed before the doctor saved the prescription | 146 of 146 | **171 of 325** |
| Current-patient polls answering "still busy" (200) | 0 of 145 | **1,268 of 3,445** |
| call-next 409 "Complete current consultation first" after 20 polls (10 s) | 0 | 29 |
| Longest delay from prescription save to queue Completed | none | about 160 s |

With QueueService's CPU pinned, its Kafka consumer fell behind the completion events by up to
about 160 s. No event was lost: at the end, 325 COMPLETE consultations matched 325 Completed
queue entries exactly.

## Post-run verification (local databases)

Read-only checks after the run (`README.md` queries), all clean:

| Check | Result |
|---|---|
| COMPLETE consultations against Completed queue entries, both ways | 325 = 325, 0 mismatches |
| Consultations with more than one prescription | 0 |
| Prescriptions without a COMPLETE consultation | 0 |
| Dispense fields inconsistent with status | 0 (311 dispensed, 13 pending) |
| Added medicine left unremoved | 0 |
| More than one InConsultation entry per doctor or per room | 0 |

The Serializable transaction kept its guarantee under heavy load: no patient was assigned
twice, and no doctor or room held two patients. The defect is that it fails with a 500 instead
of retrying or answering cleanly. At the end, 3 entries were still InConsultation (cut off by
the 600 s duration) and 5,672 of the 6,000 were still Waiting.

## Log scan

`docker compose logs`, 17:29 to 17:40 IST:

| Service | Lines | fail / error / warn |
|---|---:|---|
| queueservice | 298,988 | 1,842 unhandled `DbUpdateException` (MySQL deadlock) on call-next; no Kafka consumer errors |
| prescriptionservice | 73,550 | 0 |
| apigateway | 46,632 | 0 |
| medicalrecordservice | 0 | 0. It logs only startup lines (see the Load report) |

## Analysis

The combined Stress run breaks at about 46-62 concurrent users (about 50 req/s). That is 2.3-3×
the modelled 20-user peak, but far below the Sprint 1 (about 200 users, 355 req/s) and SWC-87
(about 195 threads, 334 req/s) knees. The reason is not the Sprint 3 code. QueueService, a
Sprint 2 service, saturates first. It has to serialise a 6,000-entry day for every counter
refresh, and its call-next transaction deadlocks under concurrent doctors.

The Sprint 3 services kept headroom throughout. PrescriptionService and MedicalRecordService
stayed mostly under 45% and 10% CPU. Every Sprint 3 endpoint stayed error-free with p95 under
1.6 s, even while the QueueService calls around them took 5-15 s. Their own breaking point is
therefore not established by this run. As planned in `TEST-PLAN.md` section 9.6, the isolated
run repeats Stress with `-JcounterQueuePoll=false` to find it. The doctor chain still depends
on call-next, so the deadlock will still limit doctor throughput there. The isolated run shows
how far it does so without QueueService's CPU pinned.

Findings for the development team:

1. **Defect: `PUT /api/queue/call-next` returns 500 on a MySQL deadlock.** It appeared with
   about 7 concurrent doctors and reached 90% of calls under load. The Serializable transaction
   needs deadlock retry (or `EnableRetryOnFailure`), and a `(QueueDate, Status)` index to
   narrow the range it locks. To be raised as a bug and linked to SWC-126.
2. **Scalability: `GET /api/queue/today` returns the whole day** (1.46 MB at 6,000 entries) on a
   5 s poll from every counter screen, with no index on `(QueueDate, Status)`. This confirms
   and quantifies the SWC-87 follow-up.
3. **Observability: MedicalRecordService writes no request or completion logs**, so its log scan
   can show only the absence of errors.

The 6,000-entry day is a stress condition, not a realistic clinic day. The deadlock, though,
needs only concurrent call-next requests, so it is a correctness issue at any volume.
