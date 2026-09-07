# Performance run report - SMOKE (Azure)

**Jira:** SWC-67  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md)

A second, network-inclusive data point against the real Azure deployment, alongside the
existing local SWC-67 results (`../../local/results/RESULT-load-20260827.md`,
`../../local/results/RESULT-stress-20260827.md`), not a replacement for them. Numbers here
include real internet round-trip time to Azure Container Apps in `eastasia` and reflect a
single `0.25 vCPU / 0.5 GiB`, `max-replicas=1` instance per service (TEST-PLAN-AZURE.md
section 7).

## Run metadata

| Field | Value |
|---|---|
| Run type | Smoke (gate for Load) |
| Date / time | 2026-09-07 12:11 IST |
| JMeter version | 5.6.3 |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=1 -Jrampup=1 -Jduration=60 -l results/smoke-azure.jtl -e -o results/smoke-azure-report` |
| Target | `https://api.swiftcare.me` (Azure Container Apps, eastasia) |
| Threads / ramp / duration | 1 / 1 s / 60 s (span 54 s) |
| Warm-up done? | yes, `results/warmup-azure.jtl` (1 user / 45 s, results discarded) |
| Cold-start observed | first `POST /api/auth/login` = 1982 ms; every other sampler 120-321 ms |
| Machine (CPU / RAM / OS) | Windows 11, load generator over the internet |
| Data volume (Azure) | 25 users, 500 patients (`seed-azure.ps1` defaults) |

## Results

### Aggregate (whole 54 s run; warm-up JVM excluded)

| Metric | Value |
|---|---|
| Total samples | 42 |
| Throughput (req/s) | 0.7 |
| Error rate (%) | 0.000 (0 / 42) |
| avg / p50 / p95 / p99 / max (ms) | 224 / 172 / 319 / 1301 / 1982 |

p99 and max are the single cold-start login; with that one sample removed, aggregate max is
321 ms.

### Per label

| Label | Samples | Error % | Codes | avg | p95 | p99 | max (ms) |
|---|---:|---:|---|---:|---:|---:|---:|
| POST /api/auth/login | 1 | 0 | 200 | 1982 | 1982 | 1982 | 1982 |
| GET /api/patients/search | 18 | 0 | 200 | 183 | 320 | 321 | 321 |
| GET /api/patients/{id} | 9 | 0 | 200 | 172 | 225 | 227 | 228 |
| GET /api/patients/{id}/allergies | 9 | 0 | 200 | 173 | 232 | 253 | 258 |
| POST /api/patients | 3 | 0 | 201 | 207 | 292 | 302 | 304 |
| POST /api/patients/{id}/allergies | 2 | 0 | 201 | 196 | 218 | 220 | 220 |

## Verdict

| Check | Result |
|---|---|
| Every sampler returned 2xx | PASS, only 200 / 201 observed |
| Zero assertion failures ("Non-2xx response") | PASS, 0 / 42 |
| `token` extracted on every login (no `LOGIN_FAILED`) | PASS, 0 `LOGIN_FAILED` markers; login returned 200 |
| Mix realised about 55 / 30 / 10 / 5 by controller execution | PASS, 18 / 9 / 3 / 2 executions = 56 / 28 / 9 / 6 |

**Overall: PASS.** Load may proceed.

## Analysis

At 1 user the deployed Sprint 1 API responds cleanly over the internet: zero errors across 42
requests, all 2xx, JWT extracted correctly, and the request mix matches the plan. Warm steady
latency is about 120 to 320 ms per request (reads and writes alike), which is the internet
round-trip to `eastasia` plus a little service time on the 0.25-vCPU instances, two orders of
magnitude above the local Smoke (single-digit ms) purely because the generator is now remote.
The only outlier is the first `POST /api/auth/login` at 1982 ms: AuthService scaling from a
cold or again-idle replica. It is a one-off onboarding cost, not a steady-state signal, and
warm-up plus the Load run's 30 s ramp absorb it. This is a network-inclusive Azure data point
alongside, not replacing, the local SWC-67 results; QueueService verification (section 6.2) is
deferred (`RESULT-queueservice-verification-azure-20260907.md`).
