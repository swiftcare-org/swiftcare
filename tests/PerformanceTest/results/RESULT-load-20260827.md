# Performance run report — LOAD

## Run metadata

| Field | Value |
|---|---|
| Run type | Load (baseline) |
| Date / time | 2026-08-27 22:47 IST |
| JMeter version | 5.6.3 |
| Command used | `jmeter -n -t swiftcare-load.jmx -q user.properties -Jthreads=20 -Jrampup=30 -Jduration=900 -l results/load.jtl -e -o results/load-report` |
| Threads / ramp / duration | 20 / 30 s / 900 s |
| Resource limits applied? | none (plain `docker compose up -d`) |
| Machine (CPU / RAM / OS) | _fill in — Windows 11, Docker Desktop_ |
| Data volume | 25 users, 500 patients (seed.ps1 defaults) |
| Git commit under test | _fill in (`git rev-parse --short HEAD` on develop)_ |
| Note | Run made after fixing `.env` KAFKA_BOOTSTRAP_SERVERS `kafka:9092` -> `kafka:29092` (see Analysis). |

## Results

### Aggregate (steady state — first 30 s and last 2 s excluded)

| Metric | Value |
|---|---|
| Total samples (whole run) | 12,068 |
| Steady-state samples | 11,781 (864 s window) |
| Throughput | 13.6 req/s |
| Error rate | 0.000% (0 errors) |
| avg / p50 / p95 / p99 / max | 7 / 5 / 8 / 12 / 199 ms |

### Per label (steady state)

| Label | Samples | Error % | avg | p50 | p95 | p99 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|
| GET /api/patients/search | 4983 | 0.00 | 6 | 5 | 8 | 9 | 23 |
| GET /api/patients/{id} | 2719 | 0.00 | 5 | 5 | 7 | 8 | 23 |
| GET /api/patients/{id}/allergies | 2719 | 0.00 | 6 | 5 | 8 | 10 | 21 |
| POST /api/patients | 905 | 0.00 | 21 | 19 | 25 | 43 | 199 |
| POST /api/patients/{id}/allergies | 455 | 0.00 | 13 | 12 | 17 | 29 | 37 |

Mix realised: 55% / 30% / 10% / 5% by controller execution — matches the plan.

### Reads vs writes

| Class | Samples | p95 (ms) | p99 (ms) | max (ms) |
|---|---:|---:|---:|---:|
| Reads (search, profile, allergies) | 10,421 | 8 | 9 | 23 |
| Writes (register, add allergy) | 1,360 | 24 | 36 | 199 |

Latency drift: first-third p95 = 19 ms, last-third p95 = 18 ms -> ratio 0.95 (no drift).

### Server-side (capture during a rerun if needed)

| | Value |
|---|---|
| `docker stats` peak CPU (per container) | _not captured — system was near-idle_ |
| MySQL `Threads_connected` / `Max_used_connections` | _fill in_ |

## Verdict — against TEST-PLAN.md §5.1

| Criterion | Threshold | Actual | Pass? |
|---|---|---:|:--:|
| Reads p95 | ≤ 800 ms | 8 ms | PASS |
| Reads p99 | ≤ 1500 ms | 9 ms | PASS |
| Writes p95 | ≤ 1500 ms | 24 ms | PASS |
| Writes p99 | ≤ 3000 ms | 36 ms | PASS |
| Error rate | ≤ 0.5% | 0.000% | PASS |
| Latency drift | ≤ 20% | −5% | PASS |

**Overall: PASS**

## Analysis

At the expected clinic peak (20 concurrent staff, realistic think time) the Sprint 1
API is effectively idle: read p95 is 8 ms and write p95 is 24 ms — two to three
orders of magnitude inside the thresholds — with zero errors over 12,068 requests
and no latency drift across 15 minutes. Throughput (13.6 req/s) is bounded by the
modelled think time, not by the server.

One defect was found and fixed before this run: the local `.env` had
`KAFKA_BOOTSTRAP_SERVERS=kafka:9092` (host listener) instead of the in-network
`kafka:29092` from `.env.example`. PatientService could not reach the broker, so
every `POST /api/patients` blocked on the publish timeout and returned `201` after
~5.0 s while logging `patient-checked-in event failed to publish after patient was
persisted`. After correcting the address, registration dropped to ~25 ms p95. Two
things worth noting independent of the config error: (1) the registration endpoint
is resilient — the patient is still persisted and a 201 returned when Kafka is
unavailable — but it pays the full publish-timeout latency on every request while
the broker is unreachable, so a real Kafka outage would make registration crawl
rather than fail; (2) local `.env` had drifted from `.env.example`.

The baseline is set. The meaningful result — where the system's knee is — comes
from the Stress run.
