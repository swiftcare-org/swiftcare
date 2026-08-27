# Performance run report — STRESS

## Run metadata

| Field | Value |
|---|---|
| Run type | Stress (find the knee) |
| Date / time | 2026-08-27 23:13 IST |
| JMeter version | 5.6.3 |
| Command | `jmeter -n -t swiftcare-load.jmx -q user.properties -Jthreads=400 -Jrampup=600 -Jduration=600 -Jthinkdelay=300 -Jthinkrange=300 -l results/stress.jtl -e -o results/stress-report` |
| Threads / ramp / duration | 400 / 600 s / 600 s (linear ramp ≈ +20 users / 30 s), think-time 300 ± 300 ms |
| Resource limits | `docker-compose.perf.yml` applied — mysql 2.0 CPU / 1 GB; authservice, patientservice, apigateway each 1.0 CPU / 512 MB |
| MySQL `max_connections` | 151 |
| Machine | Windows 11, Docker Desktop |
| Data volume | 25 users, 500 patients |
| Confirmation run | `results/stress-confirm.jtl` — 250 users, 60 s ramp, 150 s, with `docker stats` logged to `results/stress-confirm-stats.log` every ~4 s (used for the measured resource attribution below) |

## Time series (30 s windows, from `results/stress.jtl`)

| ~min | Active users | Throughput (req/s) | p50 (ms) | p95 (ms) | p99 (ms) |
|---:|---:|---:|---:|---:|---:|
| 2.5 | 121 | 301 | 8 | 21 | 27 |
| 3.0 | 141 | 317 | 29 | 190 | 317 |
| 3.5 | 161 | 342 | 68 | 241 | 355 |
| 4.0 | 181 | 332 | 101 | 481 | 761 |
| **4.5** | **202** | **355  ← peak** | 132 | 548 | 786 |
| 5.0 | 222 | 317 ↓ | 239 | 830 | 1176 |
| 5.5 | 242 | 298 ↓ | 341 | 1024 | 1408 |
| 6.5 | 283 | 302 | 477 | 1214 | 1583 |
| 7.5 | 322 | 258 | 769 | 1630 | 2010 |
| **8.5** | **363** | 256 | 887 | **2346** | 2103 |
| 9.0 | 382 | 209 | 1321 | **2430** | 2903 |
| 9.5 | 400 | 232 | 1206 | 2253 | 2678 |

Whole run: 149,014 samples, **0 errors**, overall p95 1565 ms, p99 2217 ms, max 4595 ms.

## Breaking point — against TEST-PLAN.md §5.2

Triggers, in the order they fired:

| Trigger | Fired? | Where |
|---|---|---|
| Saturation — throughput flat/falling while threads still rising | **YES — first** | Throughput peaks at **~355 req/s at ~200 active users**, then declines monotonically as users are added (355 → 317 → 298 → 258 → 232) while latency keeps climbing. |
| Aggregate p95 > 2000 ms sustained ≥ 60 s | YES — later | First 30 s window over 2000 ms at **~363 users** (p95 2346 ms); sustained from **~382 users**. |
| Error rate > 1% | **NO** | 0 errors across 149,014 requests, even at 400 users / 1.3 s average latency. |

**Breaking point = saturation at ≈ 200 concurrent users / ≈ 355 req/s.** At that point
p50 ≈ 130 ms, p95 ≈ 550 ms. Beyond it the system does not fail — it queues, and
latency degrades smoothly until p95 passes 2 s at ≈ 370 users.

## First resource to saturate — MEASURED

A confirmation run (250 users, 60 s ramp, 150 s) was made with `docker stats`
logged to `results/stress-confirm-stats.log` every ~4 s. During steady-state load
(24,531 samples, 0 errors, p50 913 ms / p95 2042 ms):

| Container | CPU cap | CPU % under load | % of cap |
|---|---:|---:|---:|
| **mysql** | 2.0 CPU (200%) | **~195–206%** | **~98–100% — both cores pinned, entire run** |
| patientservice | 1.0 CPU (100%) | ~40–55% | ~half — has headroom, waiting on the DB |
| authservice | 1.0 CPU (100%) | ~95–100% during the 60 s login ramp only, then ~0% | transient |
| apigateway | 1.0 CPU (100%) | ~12–25% | idle |

**The bottleneck is MySQL CPU.** MySQL sat at its full 2.0-CPU budget for the whole
steady-state window while every other tier had spare capacity. This corrects the
earlier inference (PatientService CPU) — the application tier is light; the work
is in the database.

AuthService reaches ~100% only while all 250 virtual users authenticate during the
ramp (`POST /api/auth/login`, once per thread). Once tokens are issued it drops to
idle, so login is a transient CPU spike during onboarding, not a steady-state
constraint. Memory was never a factor (MySQL ~53% of 1 GB, services 15–30% of
512 MB throughout). No `Threads_connected` pressure relative to
`max_connections` = 151.

**Likely cause (hypothesis, not yet verified):** `GET /api/patients/search` runs
`LIKE '%term%'` across name / NIC / phone, which cannot use a normal B-tree index
and forces row scans — CPU cost grows with table size and with the 55 % search
weight in the mix. Verifying with the MySQL slow-query log / `EXPLAIN` and adding
a suitable index (or a `FULLTEXT` index) is the recommended follow-up.

## Analysis

Under the capped test environment the Sprint 1 API saturates at roughly **200
concurrent users and ~355 requests/second** — about **10× the modelled clinic
peak** (20 users). The failure mode is graceful: throughput plateaus and latency
rises, but there are zero errors, no 5xx, no timeouts and no connection-pool
exhaustion even at 400 concurrent users and ~1.3 s average latency.

The saturating resource is **MySQL CPU** (measured, not inferred): both of its
allotted cores were pinned throughout steady-state load while PatientService
(~50 %), the Gateway (~20 %) and AuthService (idle after login) all had
headroom.

Practical implications for a real deployment, in priority order:
1. **Reduce database CPU per request first** — profile the patient-search query
   and index it; the read path is 85 % of traffic and search is 55 % of it.
2. **Then scale MySQL** (more vCPU, or read replicas) if concurrency is expected
   to approach a few hundred simultaneous users.
3. PatientService and the Gateway do **not** need scaling at this load.
4. Watch AuthService CPU during mass-login events (shift changes) — brief, but it
   does hit its cap.

At the actual target scale (≈20 concurrent users) there is roughly an order of
magnitude of headroom on every tier.

## Deviation from the pre-registered plan

The stress ceiling was raised from 80 → 400 users and think-time cut from
1000 ± 1500 ms to 300 ± 300 ms, and the run was made against the CPU/memory-capped
stack. Reason: the Load run showed the uncapped system idle at the planned peak
(read p95 8 ms, 0 errors), so the original stress profile would not have reached a
knee. The §5.2 pass/fail triggers were not changed.
