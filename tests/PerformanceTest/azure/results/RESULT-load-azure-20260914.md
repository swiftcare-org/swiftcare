# Performance run report - LOAD (Azure, Sprint 2 endpoints)

**Jira:** SWC-88  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 11

The Sprint 2 half of the deployed-environment baseline: the three queue-polling reads (SWC-23 public display, SWC-20 Receptionist full queue, SWC-21 Doctor waiting pool) and the one new write (SWC-24 consultation creation), at the same 20-user peak and against the same deployment as the Sprint 1 Azure baseline in [`RESULT-load-azure-20260907.md`](RESULT-load-azure-20260907.md). It extends that baseline to the current API surface; it does not replace it, and the Sprint 1 samplers are untouched.

## Run metadata

| Field | Value |
|---|---|
| Run type | Load (Sprint 2 endpoints) |
| Date / time | 2026-09-14 20:52 to 21:07 IST |
| JMeter version | 5.6.3, CLI (non-GUI) |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=0 -JusersDisplay=8 -JusersToday=4 -JusersWaiting=4 -JusersConsult=4 -Jrampup=30 -Jduration=900 -l results/load-swc88.jtl -e -o results/load-swc88-report` |
| Target | `https://api.swiftcare.me` (Azure Container Apps, eastasia), Gateway only |
| Threads / ramp / duration | 20 / 30 s / 900 s (actual span 895 s) |
| Thread mix | display 8, full queue 4, waiting pool 4, consultations 4 |
| Container sizing | 0.25 vCPU / 0.5 GiB per app, min-replicas 0, max-replicas 1 (no scale-out) |
| Warm-up | `results/warmup-swc88.jtl`, 32 samples, 0 errors, discarded from all figures |
| Smoke gate | PASS, [`RESULT-smoke-azure-20260914.md`](RESULT-smoke-azure-20260914.md) |
| Seeded data | 10 Receptionists, 200 patients, 70 Doctors, 70 pre-called queue entries |
| Machine | Windows 11, load generator over the internet |
| Report | `results/load-swc88-report/index.html` |

## Results

2,762 samples over 895 s, 3.1 req/s, 4 non-2xx responses (0.14% of all samples, and all four explained below). 12 `POST /api/auth/login` samples are per-thread setup and are excluded from both classes.

Because the deployment was warmed before the timed run, this run has no cold-start phase: the very first 60 s bucket already sits at the same p50 as the last one. The Sprint 1 baseline needed a documented deviation for that; this run does not, and the plan's standard measurement window (ramp plus the first 30 s discarded, t in [60 s, 895 s]) is used as written.

### Per sampler, measurement window t in [60 s, 895 s]

| Sampler | n | Non-2xx | avg | p50 | p95 | p99 | max (ms) | Median body |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `GET /api/queue/display` | 1,297 | 0 | 141 | 117 | 341 | 378 | 998 | 3.5 KB |
| `GET /api/queue/today` | 627 | 0 | 349 | 324 | 545 | 622 | 1315 | 46.7 KB |
| `GET /api/queue/today/waiting` | 636 | 0 | 253 | 231 | 444 | 481 | 1208 | 29.2 KB |
| `POST /api/consultations` | 46 | 4 | 361 | 346 | 432 | 577 | 670 | 0.7 KB |

### By class, measurement window

| Class | Samples | p95 (ms) | p99 (ms) | max (ms) |
|---|---:|---:|---:|---:|
| Reads (display, full queue, waiting pool) | 2,560 | 401 | 557 | 1315 |
| Writes (consultation creation) | 46 | 432 | 577 | 670 |

### Time series (60 s buckets, reads)

| t (s) | n | p50 (ms) | p95 (ms) | p99 (ms) | max (ms) |
|---:|---:|---:|---:|---:|---:|
| 0-60 | 144 | 219 | 372 | 684 | 966 |
| 60-120 | 183 | 216 | 361 | 528 | 626 |
| 120-180 | 187 | 209 | 360 | 547 | 577 |
| 180-240 | 185 | 213 | 385 | 524 | 591 |
| 240-300 | 183 | 217 | 367 | 543 | 554 |
| 300-360 | 183 | 219 | 454 | 959 | 1315 |
| 360-420 | 184 | 210 | 386 | 537 | 557 |
| 420-480 | 182 | 218 | 374 | 530 | 567 |
| 480-540 | 184 | 210 | 378 | 532 | 560 |
| 540-600 | 186 | 212 | 376 | 566 | 609 |
| 600-660 | 182 | 221 | 538 | 942 | 1208 |
| 660-720 | 185 | 215 | 408 | 542 | 557 |
| 720-780 | 186 | 206 | 374 | 547 | 576 |
| 780-840 | 181 | 217 | 375 | 549 | 555 |
| 840-900 | 169 | 218 | 373 | 487 | 560 |

Two single-bucket blips (t 300-360 s and t 600-660 s, p95 454 and 538 ms against a 375 ms run median) recover within the next bucket and never move p50. Both are consistent with burstable-CPU or GC noise on the single small replica, the same signature the Sprint 1 baseline recorded at t 780-810 s, and both sit far inside the threshold.

### The four non-2xx responses

All four are `409 Conflict` on `POST /api/consultations`, at t+138 s, t+142 s, t+146 s and t+157 s: the first request of each of the four write threads, and nothing after.

They are a test-data artifact, not an application fault. `data/called-queue.csv` is consumed one row per request and never recycled, and the Smoke gate that preceded this run consumed its first four rows. The Load run reopened the file at row 1, so its four opening writes targeted queue entries that already had a consultation, and the service correctly answered 409. The remaining 42 writes all returned 201. The fix is in the process, not the code, and is now written into the plan: drop the consumed rows (or re-seed) between the Smoke gate and the Load run.

## Verdict - against TEST-PLAN-AZURE.md sections 6.1 and 11.5

| Criterion | Threshold | Actual | Pass? |
|---|---|---:|:--:|
| Reads p95 | 1200 ms or less | 401 ms | PASS (3.0x margin) |
| Reads p99 | 2000 ms or less | 557 ms | PASS (3.6x) |
| Writes p95 | 2000 ms or less | 432 ms | PASS (4.6x) |
| Writes p99 | 3500 ms or less | 577 ms | PASS (6.1x) |
| Error rate | 0.5% or less | 0.153% (4 of 2,606, all the seeded-data 409s) | PASS |
| Latency drift | last-third p95 within 20% of first-third | reads 423 to 421 ms, ratio 1.00; writes ratio 0.92 | PASS |

**Overall: PASS**, on the plan's own measurement window with no deviation. Excluding the four data-artifact 409s the error rate is 0.000%.

## Comparison with the prior baseline

The Sprint 1 Azure Load run of 2026-09-07 is the reference point. Same deployment, same region, same container sizing, same 20 concurrent users, same thresholds, different endpoints, so this is a like-for-like comparison of the environment and a first measurement of the new endpoints.

| | Sprint 1 baseline, 2026-09-07 | Sprint 2, 2026-09-14 |
|---|---|---|
| Endpoints | patient search, profile, allergies, register, add allergy | queue display, full queue, waiting pool, consultation create |
| Samples | 9,888 | 2,762 |
| Throughput | 12.4 req/s | 3.1 req/s |
| Reads p95 | 1155 ms strict window, 228 ms settled window | 401 ms, strict window, no deviation needed |
| Reads p99 | 3017 ms strict (FAIL), 442 ms settled | 557 ms |
| Writes p95 | 1325 ms strict, 274 ms settled | 432 ms |
| Writes p99 | 2672 ms strict, 452 ms settled | 577 ms |
| Error rate | 0.010% | 0.153%, all four from consumed seed rows |
| Drift ratio | 0.13 strict (cold start), 1.86 settled | 1.00 |
| Cold start in window | yes, forced a documented deviation | no, the run was warmed first |

Three things the comparison shows:

1. **The environment behaves the same as it did a week ago.** Once warm, both runs sit in the low hundreds of milliseconds at 20 users with a flat p50 and occasional single-bucket blips. Nothing about the deployment has regressed.
2. **The new endpoints are slower than the Sprint 1 ones, and the payload explains it, not the code path.** Sprint 1 reads settled at p95 228 ms on responses of roughly 1 KB. The Sprint 2 reads sit at p95 401 ms on responses of 3.5 KB, 29.2 KB and 46.7 KB, and the ordering of the three tracks the payload size exactly: display 117 ms at 3.5 KB, waiting pool 231 ms at 29.2 KB, full queue 324 ms at 46.7 KB. The unauthenticated public display, the one endpoint that a waiting room hits hardest, is also the fastest of the three.
3. **The measurement is cleaner than the baseline.** Warming the deployment first removed the cold-start contamination that made the baseline fail its own strict window, so this run passes on the plan as written and its drift figure (1.00) is a real steady-state number rather than a warm-up slope.

Throughput is lower than the baseline purely by design: this workload is fixed-interval polling (about 5 s per read thread) plus a deliberately slow write sampler, not the baseline's continuous think-time loop. It is paced by the test, not by the server.

## Threshold breaches and environment constraints

No threshold was breached. For completeness, the constraints that would surface first if one were:

- Every app runs `min-replicas=0, max-replicas=1, 0.25 vCPU / 0.5 GiB`. There is no horizontal autoscaling, so 20 users hit one small replica of each service. The two latency blips above are consistent with that, and with no server-side APM they cannot be attributed further than "the single replica", which is exactly why they are not being called an application defect.
- Every figure includes real internet round-trip time from the load generator to `eastasia`; median connect time on a fresh connection was about 220 ms, which is pure network.
- No server-side metrics, no `docker stats` equivalent, no APM. JMeter client-side timings only.

## Observations for the development team

1. **`GET /api/queue/today` returns the entire day's queue, uncompressed.** At 200 rows it is already a 46.7 KB response and the slowest endpoint in the suite (p50 324 ms against the display's 117 ms). It is not a problem at this size and it is well inside the threshold, but it grows linearly with the day's patient count and it is polled every 5 s per receptionist. Worth a look at response compression, or paging or filtering by status, before daily volume grows several times over. Not a defect, and no change is indicated by this run on its own.
2. **`GET /api/queue/today/waiting` has the same shape** at 29.2 KB, for the same reason.
3. **Nothing closes a consultation.** Creating a consultation leaves its queue entry `IN_CONSULTATION`, so a doctor who has consulted once can never call another patient. That is what forces one seeded Doctor account per consultation sample and caps the write rate this suite can generate; it is a known Sprint 2 gap rather than a finding of this run, but it is the single biggest limit on future write-side performance testing.
4. **The public display returns queue numbers and rooms only.** Every one of the 1,297 sampled responses satisfied the `currentRooms` / `nextQueueNumbers` contract assertion and none carried patient identifiers, which is consistent with the privacy rule for that route.

## Out of scope, unchanged

Queue-service-level verification (row counts in `swiftcare_queue`, Kafka consumer-group lag) remains deferred for the reason recorded in [`RESULT-queueservice-verification-azure-20260907.md`](RESULT-queueservice-verification-azure-20260907.md): the database and the broker are private to the Azure VNet and are not reachable from the load generator. Stress testing against the shared deployment also remains out of scope, consistent with the existing suite's own decision.

## Data left behind

Synthetic only. This round added 10 Receptionist accounts, 70 Doctor accounts, about 200 patients, about 200 queue rows and 46 consultations to the deployed databases, and left 70 queue entries in `IN_CONSULTATION` that cannot be closed from outside the VNet. Agreed with the deployment owner before the run; flagged here so it is visible in any later queue count.
