# Performance run report - STRESS (SWC-87 queue polling, combined)

## Run metadata

| Field | Value |
|---|---|
| Run type | Stress (find the knee) - all three endpoints combined |
| Jira | SWC-87 (endpoints SWC-20, SWC-21, SWC-23) |
| Date / time | 2026-09-14 13:04 IST |
| JMeter version | 5.6.3 |
| Command | `jmeter -n -t SWC-87-queue-polling.jmx -q user.properties -JusersDisplay=150 -JusersToday=100 -JusersWaiting=100 -JrampUp=600 -Jduration=600 -JpollDelay=300 -JpollRange=300 -Jhost=localhost -Jport=8000 -l results/stress-SWC-87.jtl -e -o results/stress-SWC-87-report` |
| Threads / ramp / duration | 150 (display) + 100 (today-queue) + 100 (waiting-pool) = 350 peak, linear ramp over 600 s, 600 s duration (ramp *is* the run), poll interval cut to 300 ± 300 ms |
| Resource limits | `docker-compose.perf.yml` applied, extended this ticket to add **queueservice: 1.0 CPU / 512 MB** (the Sprint 1 version of this file did not cap QueueService - out of scope then) alongside mysql 2.0 CPU/1 GB, authservice/patientservice/apigateway each 1.0 CPU/512 MB |
| Machine | Windows 11, Docker Desktop |
| Data volume | 219 today-dated queue entries (190 Waiting, 29 InConsultation), 10 Doctor + 10 Receptionist perf accounts |

## Calibration

The Load run (`RESULT-load-SWC-87-20260914.md`) showed the uncapped system idle at
15/10/10 users (p95 11-14 ms, every container's CPU under 6%). A ceiling at that
scale would not reach a knee, so - following the same reasoning already applied in
`TEST-PLAN.md` §5.2 for the Sprint 1 suite - the ceiling was scaled up roughly 10×,
keeping the ticket's 15:10:10 ratio: **150 / 100 / 100 = 350 peak concurrent
users**, ramped linearly over the full 10-minute run against the resource-capped
stack, with poll interval cut from ~5 s to 300 ± 300 ms so request rate actually
climbs with concurrency.

## Time series (30 s windows, from `results/stress-SWC-87.jtl`)

| ~min | Active threads | Throughput (req/s) | p95 (ms) |
|---:|---:|---:|---:|
| 1.0 | 38 | 60 | 47 |
| 2.0 | 73 | 133 | 22 |
| 3.0 | 108 | 211 | 35 |
| **3.5** | 143 | 261 | 180 |
| **4.0** | 160 | 296 | 149 |
| 5.0 | 195 | **334 ← peak** | 258 |
| 5.5 | 213 | 303 ↓ | 693 |
| 6.5 | 248 | 297 | 588 |
| 7.5 | 283 | 269 | 1320 |
| 8.5 | 318 | 294 | 908 |
| 9.5 | 350 | 289 | 1150 |

Whole run: 143,043 samples, **0 errors, every sample HTTP 200**, overall avg 287 ms.
Last 60 s (283-350 threads, all three endpoints): p50 ≈ 655 ms, p95 ≈ 1,090-1,110 ms,
p99 ≈ 1,320-1,372 ms, max ≈ 1,590 ms - evenly spread across all three endpoints
(display, today-queue, waiting-pool), consistent with one shared bottleneck.

## Breaking point - against the ticket's criteria

| Trigger | Fired? | Where |
|---|---|---|
| Saturation - throughput flat/falling while threads still rising | **YES** | Throughput peaks at **~334 req/s at ~195 active threads (~t=300s)**, then holds in a 269-320 req/s band for the rest of the run (~300 s, well over the 60 s threshold) while threads keep climbing to 350. |
| Aggregate p95 > 2000 ms sustained ≥ 60 s | **NO** | p95 rose into the 900-1,370 ms range by the end but never sustained past 2000 ms at any point in this run. |
| Error rate > 1% | **NO** | 0 errors across 143,043 requests, even at 350 concurrent threads and ~700 ms average sample time. |

**Breaking point = throughput saturation at ≈195 active threads / ≈334 req/s
(~t=300s into the ramp).** Beyond it the system does not fail - it queues, exactly
as the Sprint 1 stress run found: latency degrades smoothly (p50 rising toward
~650 ms, p95 toward ~1.1 s by 350 threads) with zero errors, no 5xx, no timeouts.
This run did not reach the aggregate-p95 or error-rate triggers within the
calibrated 350-user ceiling; the throughput plateau is the trigger that fired.

## First resource to saturate - MEASURED

`docker stats` was sampled every ~55 s for the run duration (`stress-SWC-87-docker-stats.log`):

| Container | CPU cap | CPU % under load | % of cap |
|---|---:|---:|---:|
| **queueservice** | 1.0 CPU (100%) | **~82-102%, from ~t=180s (≈125-145 threads) through the end of the run** | **~100% - pinned for ~420 s of the 600 s run** |
| mysql | 2.0 CPU (200%) | ~38-76%, rising slowly | headroom throughout |
| apigateway | 1.0 CPU (100%) | 28-65%, noisy | headroom |
| authservice | 1.0 CPU (100%) | brief spikes to ~60% during login bursts, else ~0% | transient (once-per-thread login) |
| patientservice | 1.0 CPU (100%) | ~0-2% | idle (not exercised by this suite) |

**The bottleneck is QueueService CPU.** It pins at or above its single-core cap
well before the throughput plateau shows up in the JMeter time series (saturated
by ~t=180s / ~130 threads; throughput does not visibly flatten until ~t=300s /
~195 threads) - QueueService runs out of CPU first, and the plateau downstream of
that is the visible symptom. MySQL, apigateway and authservice all had headroom
for the entire run; patientservice is idle because nothing in this suite calls it.

All three endpoints - `GET /api/queue/display`, `GET /api/queue/today`,
`GET /api/queue/today/waiting` - read the same `QueueEntries` table inside the
same QueueService process, so they compete for the same capped CPU budget; the
near-identical p95 figures across all three in the last 60 s (1,086 / 1,104 /
1,109 ms) confirm that this is one shared bottleneck, not three independent ones.

**Likely cause (hypothesis, not yet verified):** as already flagged in
`RESULT-SWC-23-display-local-20260913.md`, `GetTodayAsync` / `GetWaitingAsync` /
`GetDisplayAsync` in `TodayQueueService` all run a
`Where(QueueDate == today && ...)` scan with no supporting index and no `LIMIT`,
re-executed by every one of the three endpoints on every poll. At 219 rows this is
still cheap in absolute terms, but it is CPU-per-request that does not amortize -
each of the ~300 req/s at the plateau pays the same scan cost, which is consistent
with a single vCPU running out well before MySQL, the gateway or any other tier.
Confirming this needs `EXPLAIN` against `QueueEntries` and a slow-query log pass
once a realistic row count exists (see Follow-up below); adding a composite index
on `(QueueDate, Status)` is the likely fix if confirmed.

Per-endpoint isolated runs (same peak concurrency, same capped stack, other two
groups set to 0 users) are in `RESULT-stress-SWC-87-per-endpoint-20260914.md` -
none of the three reaches this breaking point alone, confirming it belongs to the
combined traffic sharing QueueService's CPU, not to any one endpoint.

## Analysis

Under the CPU/memory-capped stack, the three queue-polling endpoints combined
saturate at roughly **195 concurrent polling sessions and ~334 requests/second**
- against the ticket's Load profile of 35 total concurrent sessions, that is
roughly **5.5× the modelled peak**. As in the Sprint 1 stress finding, the failure
mode here is graceful degradation, not breakage: latency climbs and throughput
plateaus, but there are zero errors and no failed assertions across the entire
143,043-sample run, even at double the saturation concurrency (350 threads).

The saturating resource is QueueService's capped CPU, not MySQL, not the gateway,
and not the other two services (idle). This is a materially different finding
from the Sprint 1 stress run, where MySQL CPU was the bottleneck for the
Auth/Patient slice - queue polling stresses the application tier first, because
QueueService is doing repeated unindexed table scans per request rather than
mostly waiting on the database.

Practical implication: if queue polling load grows (more waiting-room screens,
more staff, or a busier clinic day), QueueService is the first thing that needs
either a CPU increase or the query-side fix above - not MySQL.

## Follow-up (per DoD)

This run used 219 today-dated queue entries - a realistic count for a quiet clinic
day, but well short of a genuinely busy one. Because the identified bottleneck is
a CPU-bound table scan whose cost grows with row count, **this result should be
re-run once the queue table holds a production-scale row count for a single day**
(the SWC-23 result doc flagged the same limitation for the display endpoint alone,
and this stress run extends that concern to SWC-20 and SWC-21 as well - a larger
table is expected to lower the concurrency at which QueueService's CPU saturates,
not raise it).
