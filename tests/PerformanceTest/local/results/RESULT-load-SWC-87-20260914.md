# Performance run report - LOAD (SWC-87 queue polling)

## Run metadata

| Field | Value |
|---|---|
| Run type | Load |
| Jira | SWC-87 (endpoints SWC-20, SWC-21, SWC-23) |
| Date / time | 2026-09-14 12:52 IST |
| JMeter version | 5.6.3 |
| Command used | `jmeter -n -t SWC-87-queue-polling.jmx -q user.properties -JusersDisplay=15 -JusersToday=10 -JusersWaiting=10 -JrampUp=15 -Jduration=600 -Jhost=localhost -Jport=8000 -l results/load-SWC-87.jtl -e -o results/load-SWC-87-report` |
| Threads / ramp / duration | 15 (display) + 10 (Receptionist today-queue) + 10 (Doctor waiting-pool) = 35 total / 15 s / 600 s steady state |
| Resource limits applied? | none (plain `docker compose up -d`) |
| Machine (CPU / RAM / OS) | Windows 11, Docker Desktop |
| Data volume | 219 today-dated queue entries (190 Waiting, 29 InConsultation) via `seed-queue-polling.ps1`, 10 Doctor + 10 Receptionist perf accounts |

## Results

### Aggregate (whole run - all three endpoints combined)

| Metric | Value |
|---|---|
| Total samples | 4,120 |
| Errors | 0 (0.00%) |
| Throughput | 6.9 req/s |
| avg / min / max | 11 / 6 / 410 ms |

### Per endpoint

| Label | Samples | Error % | avg | p50 | p95 | p99 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|
| GET /api/queue/display (SWC-23, 15 users) | 1,768 | 0.00 | 9 | 9 | 11 | 13 | 18 |
| GET /api/queue/today (SWC-20, 10 users) | 1,165 | 0.00 | 11 | 11 | 14 | 17 | 35 |
| GET /api/queue/today/waiting (SWC-21, 10 users) | 1,167 | 0.00 | 11 | 11 | 13 | 16 | 22 |
| POST /api/auth/login (setup, 20 logins) | 20 | 0.00 | 284 | 273 | 323 | 410 | 410 |

Login is a once-per-thread setup step (bcrypt verification), not part of the polled
request mix, and is not held to the 3000 ms polling target - consistent with
`TEST-PLAN.md` A3.

### Server-side (`docker stats`, sampled every ~55s for the run duration)

| Container | CPU % range | Memory |
|---|---|---|
| apigateway | 1.3 - 2.8% | ~86 MiB, flat |
| queueservice | 2.8 - 6.0% | ~112-119 MiB, flat |
| mysql | 1.8 - 2.8% | ~515 MiB, flat |
| authservice | 0.01 - 0.05% | ~150 MiB, flat |
| patientservice | 0.08 - 0.20% | ~147 MiB, flat |

Full samples: `load-SWC-87-docker-stats.log`. No container showed rising CPU or
memory across the run - all three services were effectively idle at this profile.

## Verdict - against SWC-87 Load acceptance criteria

| Criterion | Threshold | Actual (worst of the three endpoints) | Pass? |
|---|---|---:|:--:|
| Response time at target concurrency | < 3000 ms | 35 ms max (today-queue) | PASS |
| Error rate | 0% | 0.00% | PASS |
| Every sample HTTP 200 with expected fields | required | 4,100/4,100 polling samples (excl. logins) | PASS |

**Overall: PASS** for SWC-20, SWC-21 and SWC-23 individually and combined.

## Analysis

At the ticket's Load profile (15 public-display + 10 Receptionist + 10 Doctor,
polling at the real ~5s interval, 10 minutes steady state) all three endpoints are
one to two orders of magnitude under the 3000 ms target, with zero errors across
4,120 samples and flat, low server-side resource usage throughout. The three
endpoints share one MySQL instance and one table (`QueueEntries`) and are read
concurrently by design in this run (aggregate polling load, not per-endpoint
isolation) - the combined picture is the one that matters here, since that is how
they actually run against each other in production.

This result, like the SWC-23 solo baseline it builds on, is bounded by the local
row count (219 today-dated entries) rather than by any real capacity limit - the
margin is too large to say anything about where the knee is. That is what the
Stress run is for.
