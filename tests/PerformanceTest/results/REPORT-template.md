# Performance run report — <SMOKE | LOAD | STRESS>

Copy this file to `RESULT-<type>-<yyyymmdd>.md` and fill it in. The raw `.jtl`
and generated `-report/` folders are git-ignored; this filled-in file is the
committed record.

## Run metadata

| Field | Value |
|---|---|
| Run type | |
| Date / time | |
| JMeter version | |
| Command used | `jmeter -n -t swiftcare-load.jmx -q user.properties -Jthreads=… -Jrampup=… -Jduration=… -l results/….jtl -e -o results/…-report` |
| Threads / ramp / duration | |
| Resource limits applied? | none / `docker-compose.perf.yml` (record values) |
| Machine (CPU / RAM / OS) | |
| Data volume (users / patients) | |
| Git commit under test | |

## Results

### Aggregate

| Metric | Value |
|---|---|
| Total samples | |
| Throughput (req/s) | |
| Error rate (%) | |
| p50 / p95 / p99 (ms) | |

### Per label (from the Statistics table)

| Label | Samples | Error % | p95 (ms) | p99 (ms) |
|---|---|---|---|---|
| POST /api/auth/login | | | | |
| GET /api/patients/search | | | | |
| GET /api/patients/{id} | | | | |
| GET /api/patients/{id}/allergies | | | | |
| POST /api/patients | | | | |
| POST /api/patients/{id}/allergies | | | | |

### Reads vs writes (per §5 measurement rules, ramp + first 30 s excluded)

| Class | p95 (ms) | p99 (ms) |
|---|---|---|
| Reads (search, profile, allergies) | | |
| Writes (register, add allergy) | | |

Latency drift: first-third p95 = ___ ms, last-third p95 = ___ ms → within 20%? Y/N

## Verdict

### Load run — against TEST-PLAN.md §5.1

| Criterion | Threshold | Actual | Pass? |
|---|---|---|---|
| Reads p95 | ≤ 800 ms | | |
| Reads p99 | ≤ 1500 ms | | |
| Writes p95 | ≤ 1500 ms | | |
| Writes p99 | ≤ 3000 ms | | |
| Error rate | ≤ 0.5% | | |
| Latency drift | ≤ 20% | | |

**Overall: PASS / FAIL**

### Stress run — against TEST-PLAN.md §5.2

| At breaking point | Value |
|---|---|
| Active threads | |
| Throughput (req/s) | |
| Aggregate p95 (ms) | |
| Error rate (%) | |
| Trigger that fired | p95 > 2000 ms / error > 1% / saturation |
| First resource to saturate | e.g. patientservice CPU 100%, MySQL Threads_connected = max_connections |

Evidence: `docker stats` snapshot, MySQL status output, relevant dashboard charts.

## Analysis

_2–5 sentences: what the numbers say, where and why it broke, and one concrete
recommendation (e.g. raise container CPU, tune the connection pool, add an index)._
