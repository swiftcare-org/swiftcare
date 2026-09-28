# Performance run report - LOAD REPEAT (Azure, Sprint 3 clinical flow)

**Jira:** SWC-127  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 12.7, "Repeat of run 1"

This is the official SWC-127 Load result. It is an identical repeat of run 1 ([`RESULT-load-azure-20260927.md`](RESULT-load-azure-20260927.md)): the same seed, warm-up, Smoke gate, Load profile, thresholds and measurement window. The repeat, its one precondition and the rule that it is final were written into the plan before it ran. Both runs are reported below.

## Headline

**PASS, 6 of 6 criteria, on the plan's own measurement window with no deviation.** 20 concurrent users (10 Doctor workflow, 10 Receptionist counter), **30 s ramp-up, 900 s run**, **4,915 samples at 5.5 req/s**, **0 errors**. In 15 minutes, 140 patients were called, 140 prescriptions written and 132 dispensed.

| Criterion (t ≥ 60 s) | Measured | Limit | Result |
|---|---:|---:|:--:|
| Reads p95 / p99 | **193 / 255 ms** | 1,200 / 2,000 ms | PASS |
| Writes p95 / p99 | **239 / 294 ms** | 2,000 / 3,500 ms | PASS |
| Error rate | **0.00%** (0 of 4,915) | 0.5% | PASS |
| Latency drift (last / first third p95) | **0.96** reads, **0.99** writes | within 20% | PASS |

The slowest sample of the whole run, excluding the 20 setup logins, was 656 ms. No sample took longer than 5 s.

## Run metadata

| Field | Value |
|---|---|
| Run type | Load, identical repeat of run 1 |
| Date / time | 2026-09-28 00:10:36 to 00:25:36 IST |
| JMeter version | 5.6.3, CLI (non-GUI), on Java 21.0.12 |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=0 -JusersClinicDoctor=10 -JusersClinicCounter=10 -Jrampup=30 -Jduration=900 -l results/load-swc127-repeat.jtl -e -o results/load-swc127-repeat-report` |
| Target | `https://api.swiftcare.me`, through Cloudflare, to Azure Container Apps (eastasia) |
| Threads / ramp / duration | 10 Doctor + 10 Receptionist = 20 / 30 s / 900 s (actual span 899.9 s) |
| Pacing | As run 1: Doctor 1-3 s between steps, 40-50 s before the next patient; Receptionist queue and pending list every 4.5-5.5 s, dispense 30 s or more after a save |
| Deployment | Same develop images as run 1. The environment had been stopped (apps stopped, Kafka removed, MySQL stopped) and started again before the repeat. Every app 0.25 vCPU / 0.5 GiB, one replica; MySQL `Standard_B1ms`. |
| Precondition | 20 consecutive `GET /health` requests answered 200 in under 1 s before the warm-up (plan 12.7) |
| Warm-up | As run 1: 1 + 1 users, 60 s, `results/warmup-swc127-repeat.jtl`: 52 samples, 0 errors, all 17 labels, discarded |
| Smoke gate | PASS: 1 + 1 users, 60 s, 53 samples, 0 failures, all 17 labels, p95 221 ms (`results/smoke-swc127-repeat-report`) |
| Seeded data | `seed-clinical-flow-azure.ps1`, 170 patients checked in on 2026-09-28; doctors reset with `-QueueVolume 0` after the warm-up and after Smoke |
| Cloudflare | Rate-limiting rule disabled by DevOps for the test window, as for run 1 |
| Report | `results/load-swc127-repeat-report/index.html` |

Run 1 was run on a day whose queue already held about 200 entries. The repeat ran on a new clinic day. The payloads that depend on that are close: `GET /api/queue/today` was 42-49 KB in the repeat against 50-56 KB in run 1, and the pending list was 1-10 KB in both.

## Results

Reads and writes are the classes in `TEST-PLAN-AZURE.md` 12.1. The Sprint 2 endpoints the workflow depends on are reported separately. The 20 logins are setup (1.4-1.9 s) and are excluded.

### Per endpoint, plan window (t ≥ 60 s, 840 s, 4,621 samples)

| Class | Label | n | Errors | avg | p50 | p95 | p99 | max (ms) |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Read | GET /api/prescriptions/pending | 1,464 | 0 | 128 | 121 | 190 | 258 | 348 |
| Read | GET /api/consultations/patient/{id}/latest-follow-up | 130 | 0 | 137 | 120 | 237 | 281 | 656 |
| Read | GET /api/consultations/latest-completed | 130 | 0 | 127 | 119 | 198 | 240 | 250 |
| Read | GET /api/prescriptions/patient/{id} | 130 | 0 | 126 | 120 | 179 | 241 | 318 |
| Read | GET /api/prescriptions/queue/{queueId} | 131 | 0 | 128 | 121 | 181 | 220 | 236 |
| Write | POST /api/consultations/{id}/vitals | 130 | 0 | 138 | 126 | 219 | 287 | 296 |
| Write | POST /api/consultations/{id}/complete | 130 | 0 | 158 | 145 | 237 | 284 | 307 |
| Write | POST /api/prescriptions | 130 | 0 | 156 | 145 | 232 | 259 | 267 |
| Write | POST /api/prescriptions/{id}/items | 130 | 0 | 150 | 136 | 238 | 261 | 279 |
| Write | DELETE /api/prescriptions/{id}/items/{medicineId} | 130 | 0 | 150 | 138 | 244 | 329 | 405 |
| Write | PUT /api/prescriptions/{id}/dispense | 130 | 0 | 158 | 138 | 244 | 303 | 348 |
| Sprint 2 | GET /api/queue/today | 1,465 | 0 | 210 | 205 | 302 | 353 | 632 |
| Sprint 2 | GET /api/queue/today/current | 131 | 0 | 195 | 188 | 272 | 339 | 346 |
| Sprint 2 | PUT /api/queue/call-next | 130 | 0 | 155 | 146 | 240 | 267 | 285 |
| Sprint 2 | POST /api/consultations | 130 | 0 | 135 | 131 | 153 | 252 | 269 |

Every sample returned its expected status. No Duration Assertion fired.

### By class

| Window | Class | Samples | p50 | p95 | p99 | max (ms) | Drift |
|---|---|---:|---:|---:|---:|---:|---:|
| Plan (t ≥ 60 s) | Reads | 1,985 | 121 | 193 | 255 | 656 | 196 → 189 ms (0.96) |
| Plan (t ≥ 60 s) | Writes | 780 | 138 | 239 | 294 | 405 | 237 → 235 ms (0.99) |
| From t ≥ 180 s, for comparison with run 1 | Reads | 1,698 | 121 | 200 | 259 | 656 | 0.90 |
| From t ≥ 180 s, for comparison with run 1 | Writes | 661 | 139 | 243 | 296 | 405 | 0.94 |

The whole run including ramp-up: 4,915 samples, 0 errors, p50 134 ms, p95 274 ms, p99 339 ms, 5.5 req/s. The fastest sample was 98 ms, which is the network floor through Cloudflare to eastasia.

### Time series (60 s buckets)

| Minute | Samples | p95 all | p95 reads | p95 writes | max (ms) |
|---:|---:|---:|---:|---:|---:|
| 0 | 294 | 208 | 162 | 208 | 338 |
| 1 | 341 | 202 | 164 | 156 | 315 |
| 2 | 333 | 247 | 171 | 236 | 387 |
| 3 | 333 | 269 | 198 | 196 | 478 |
| 4 | 328 | 269 | 246 | 244 | 632 |
| 5 | 333 | 276 | 204 | 264 | 353 |
| 6 | 320 | 294 | 186 | 261 | 431 |
| 7 | 323 | 279 | 193 | 206 | 350 |
| 8 | 326 | 277 | 217 | 253 | 342 |
| 9 | 331 | 279 | 193 | 216 | 359 |
| 10 | 326 | 280 | 159 | 191 | 656 |
| 11 | 327 | 275 | 220 | 235 | 348 |
| 12 | 338 | 268 | 169 | 239 | 342 |
| 13 | 332 | 272 | 168 | 329 | 405 |
| 14 | 330 | 277 | 228 | 207 | 436 |

Flat from the first minute to the last: no settling phase, no stall, no spike.

### Server side (Azure Monitor, 1-minute maxima over the run)

| Resource | CPU | Memory | Other |
|---|---|---|---|
| swiftcare-gateway | 0-7% of 0.25 vCPU | 105 MiB | 0 restarts |
| swiftcare-auth | 0-48% (login bursts only) | 139 MiB | 0 restarts |
| swiftcare-patient | 0-1% | 166 MiB | 0 restarts |
| swiftcare-queue | 1-7% | 181 MiB | 0 restarts |
| swiftcare-medical-record | 0-5% | 123 MiB | 0 restarts |
| swiftcare-prescription | 0-6% | 137 MiB | 0 restarts |
| swiftcare-mysql (B1ms) | 15-17% | 25% | |

## Verdict - against TEST-PLAN-AZURE.md section 12.5

| Criterion | Threshold | Actual (plan window) | Margin | Result |
|---|---|---:|---:|:--:|
| Reads p95 | 1,200 ms or less | 193 ms | 6.2x | PASS |
| Reads p99 | 2,000 ms or less | 255 ms | 7.8x | PASS |
| Writes p95 | 2,000 ms or less | 239 ms | 8.4x | PASS |
| Writes p99 | 3,500 ms or less | 294 ms | 11.9x | PASS |
| Error rate | 0.5% or less | 0.00% | - | PASS |
| Latency drift | within 20% | 0.96 reads / 0.99 writes | - | PASS |

**Overall: PASS, 6 of 6**, on the plan's standard window as written. Every Sprint 3 read and write completed.

## Run 1 and the repeat

Both used the identical procedure, deployment and code.

| | Run 1 (2026-09-27 20:23) | Repeat (2026-09-28 00:10) |
|---|---:|---:|
| Samples / throughput | 4,612 / 5.1 req/s | 4,915 / 5.5 req/s |
| Reads p95 / p99 (t ≥ 60 s) | 704 / 4,277 ms | **193 / 255 ms** |
| Writes p95 / p99 (t ≥ 60 s) | 1,202 / 3,527 ms | **239 / 294 ms** |
| Errors (response code) | 3 call-next 500s (0.07%) | **0** |
| Samples slower than 5 s | 44 | **0** |
| Slow phase | about 3 minutes after load started | none |
| Criteria met, plan window | 3 of 6 | **6 of 6** |
| Azure CPU during the run | every app under 40%, MySQL 16-19% | every app under 48%, MySQL 15-17% |

In both runs the platform was idle: no app or database came near its limit. The deployment, code, workload and procedure were identical, and the repeat showed no slow phase. Run 1's slow phase therefore did not come from SwiftCare's capacity. It is consistent with the network path between the load generator and Azure, the same cause confirmed for the discarded attempt below. Its exact cause in run 1 cannot be proven after the fact. Run 1's three call-next 500s all fell inside its slow phase, when transactions were held open longer. The repeat saw none. SWC-128 still stands, because the deadlock is reproduced locally under Stress.

**Discarded attempt.** An attempt at 2026-09-27 23:29 IST, with a full-size warm-up, was discarded because the load generator's connection was unstable for the first half of the run (recorded in plan 12.7 before this repeat).

**Smoke retried after midnight.** The first Smoke attempt of the repeat started a few minutes after midnight clinic time, so the waiting patients seeded before midnight belonged to the previous day and call-next returned 404 "No patients currently waiting". The queue was re-seeded for 2026-09-28, and the warm-up and Smoke were run again; this report uses that sequence.

## Comparison

| | Local SWC-126 (same workload) | Azure Sprint 2 (SWC-88) | Azure Sprint 3 (this repeat) |
|---|---|---|---|
| Users / duration | 20 / 900 s | 20 / 900 s | 20 / 900 s |
| Samples / throughput | 5,161 / 5.7 req/s | 2,762 / 3.1 req/s | 4,915 / 5.5 req/s |
| Reads p95 / p99 | 14 / 22 ms | 401 / 557 ms | 193 / 255 ms |
| Writes p95 / p99 | 42 / 54 ms | 432 / 577 ms | 239 / 294 ms |
| Error rate | 0.00% | 0.153% (seed-data 409s) | 0.00% |
| Drift | 1.00 | 1.00 | 0.96 / 0.99 |

- **Against local:** the Azure p50 of about 120-145 ms against 9-37 ms locally is the network floor (98 ms minimum) plus the 0.25 vCPU replicas. That is the deployed-environment cost, not a code difference.
- **Against Sprint 2:** the Sprint 3 clinical endpoints are faster at p95 and p99 than Sprint 2's queue endpoints were, partly because their payloads are small (1-10 KB against up to 47 KB).

## Findings

1. **The deployed Sprint 3 workflow meets every Azure criterion at the modelled clinic peak**, with 6-12x margin on latency and no errors.
2. **Measure from a stable connection.** Two of three attempts showed slow phases that the platform metrics do not explain, and one was traced to the load generator's connection. Future Azure rounds should keep the `/health` connection check as a precondition.
3. **Cloudflare rate limiting** blocks bursts of logins from one IP (error 1015) and had to be disabled for testing. It should be reviewed before real use, since staff signing in together behind one office IP could trigger it.
4. **SWC-128** (call-next deadlock, Sprint 4) did not occur in this run but remains open, reproduced by the local Stress run.

## Data footprint

The repeat, its warm-up and Smoke added to the deployed databases:

- 170 patients and queue entries;
- about 146 consultations with vital signs, about 146 completed queue entries, and about 146 prescriptions, most of them dispensed.

All of it is synthetic, on top of the earlier runs' data, and the same agreement with DevOps applies.
