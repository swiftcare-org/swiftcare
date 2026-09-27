# Performance run report - LOAD (Azure, Sprint 3 clinical flow)

**Jira:** SWC-127  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 12

> **Superseded.** The official SWC-127 Load result is [`RESULT-load-azure-20260928.md`](RESULT-load-azure-20260928.md), an identical repeat of this run. This report is kept as the earlier run.

The Sprint 3 part of the deployed-environment baseline: the clinical workflow at the modelled clinic peak, against the live Azure deployment. It extends the Sprint 1 ([`RESULT-load-azure-20260907.md`](RESULT-load-azure-20260907.md)) and Sprint 2 ([`RESULT-load-azure-20260914.md`](RESULT-load-azure-20260914.md)) baselines, and runs the same workload as the local Sprint 3 run ([`../../local/results/RESULT-load-SWC-126-20260927.md`](../../local/results/RESULT-load-SWC-126-20260927.md)).

## Headline

**20 concurrent users** (10 Doctor workflow, 10 Receptionist counter), **30 s ramp-up, 900 s run**, **4,612 samples at 5.1 req/s**. In 15 minutes, 136 patients were called, 132 prescriptions written and 128 dispensed.

| | Settled window, t ≥ 180 s (deviation, as 2026-09-07) | Plan window, t ≥ 60 s | Limit |
|---|---:|---:|---:|
| Reads p95 / p99 | **353 / 1,553 ms** | 704 / **4,277** ms | 1,200 / 2,000 ms |
| Writes p95 / p99 | **482 / 2,008 ms** | 1,202 / **3,527** ms | 2,000 / 3,500 ms |
| Error rate (response code) | **0.00%** | 0.07% (3 call-next 500s) | 0.5% |
| Latency drift (last / first third p95) | **1.22** reads, **1.22** writes | 0.10 / 0.13 (settling) | within 20% |

**Verdict:**

- **Settled window: 5 of 6 criteria pass.** Every latency and error criterion passes. Latency drift is **1.22**, just outside the 1.20 allowance: a rise of about 50 ms at p95.
- **Plan window: fails on both p99s.** The first three minutes carried a settling phase with isolated 5-16 s responses.

This is not a clean pass. The report states both windows so the reader can judge.

## Run metadata

| Field | Value |
|---|---|
| Run type | Load (Sprint 3 clinical flow) |
| Date / time | 2026-09-27 20:23:00 to 20:38:00 IST |
| JMeter version | 5.6.3, CLI (non-GUI), on Java 21.0.12 |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=0 -JusersClinicDoctor=10 -JusersClinicCounter=10 -Jrampup=30 -Jduration=900 -l results/load-swc127.jtl -e -o results/load-swc127-report` |
| Target | `https://api.swiftcare.me`, through Cloudflare (Colombo edge), to Azure Container Apps (eastasia) |
| Threads / ramp / duration | 10 Doctor + 10 Receptionist = 20 / 30 s / 900 s (actual span 899.7 s) |
| Pacing | Doctor: 1-3 s between steps, 40-50 s before the next patient. Receptionist: queue and pending list every 4.5-5.5 s, dispense 30 s or more after a save. |
| Deployment | Current `develop` deployed by CD. Every app 0.25 vCPU / 0.5 GiB, one replica. MySQL Flexible Server `Standard_B1ms` (Burstable). Kafka recreated by Terraform the same day. |
| Warm-up and Smoke | Warm-up 49 samples, 0 errors, discarded. Smoke PASS, [`RESULT-smoke-azure-20260927.md`](RESULT-smoke-azure-20260927.md). Doctors reset with `-QueueVolume 0` after each. |
| Seeded data | `seed-clinical-flow-azure.ps1`: 10 Doctor + 10 Receptionist accounts, 200 patients checked in on the day of the run |
| Cloudflare | Rate-limiting rule disabled by DevOps for the test window (see Environment findings) |
| Machine | Windows 11, load generator over the internet |
| Report | `results/load-swc127-report/index.html` |

## Results

Reads and writes are the classes in `TEST-PLAN-AZURE.md` 12.1. The Sprint 2 endpoints the workflow depends on are reported separately and are not part of pass/fail. The 20 logins are setup (1.5-2.7 s each, BCrypt on 0.25 vCPU) and are excluded.

### Per endpoint, settled window (t ≥ 180 s, 720 s, 3,841 samples)

| Class | Label | n | Errors | avg | p50 | p95 | p99 | max (ms) |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Read | GET /api/prescriptions/pending | 1,211 | 3 (duration) | 199 | 147 | 337 | 1,486 | 5,061 |
| Read | GET /api/consultations/patient/{id}/latest-follow-up | 109 | 0 | 192 | 151 | 295 | 478 | 1,441 |
| Read | GET /api/consultations/latest-completed | 108 | 0 | 224 | 155 | 525 | 1,730 | 2,000 |
| Read | GET /api/prescriptions/patient/{id} | 108 | 0 | 228 | 149 | 709 | 1,659 | 1,754 |
| Read | GET /api/prescriptions/queue/{queueId} | 109 | 2 (duration) | 235 | 143 | 379 | 2,086 | 4,289 |
| Write | POST /api/consultations/{id}/vitals | 108 | 0 | 218 | 163 | 429 | 937 | 2,033 |
| Write | POST /api/consultations/{id}/complete | 108 | 0 | 231 | 191 | 307 | 1,732 | 1,774 |
| Write | POST /api/prescriptions | 110 | 0 | 295 | 178 | 908 | 2,188 | 3,175 |
| Write | POST /api/prescriptions/{id}/items | 112 | 0 | 263 | 167 | 998 | 2,044 | 2,295 |
| Write | DELETE /api/prescriptions/{id}/items/{medicineId} | 112 | 1 (duration) | 274 | 169 | 748 | 1,652 | 3,625 |
| Write | PUT /api/prescriptions/{id}/dispense | 109 | 0 | 193 | 155 | 374 | 746 | 1,129 |
| Sprint 2 | GET /api/queue/today | 1,211 | 0 | 326 | 280 | 542 | 2,027 | 4,359 |
| Sprint 2 | GET /api/queue/today/current | 109 | 0 | 316 | 257 | 637 | 814 | 967 |
| Sprint 2 | PUT /api/queue/call-next | 109 | 0 | 219 | 189 | 388 | 477 | 928 |
| Sprint 2 | POST /api/consultations | 108 | 0 | 223 | 170 | 341 | 639 | 3,204 |

No non-2xx response in the settled window. The 6 errors counted are Duration Assertion tripwires, reported apart from the error rate as the plan requires.

### By class

| Window | Class | Samples | p50 | p95 | p99 | max (ms) |
|---|---|---:|---:|---:|---:|---:|
| Settled (t ≥ 180 s) | Reads | 1,645 | 148 | 353 | 1,553 | 5,061 |
| Settled (t ≥ 180 s) | Writes | 659 | 170 | 482 | 2,008 | 3,625 |
| Plan (t ≥ 60 s) | Reads | 1,849 | 148 | 704 | 4,277 | 14,033 |
| Plan (t ≥ 60 s) | Writes | 739 | 170 | 1,202 | 3,527 | 8,452 |

The whole run including ramp-up: 4,612 samples, 3 non-2xx (all call-next 500s), 57 duration breaches, p50 190 ms, p95 1,068 ms. The fastest possible round trip was 106 ms, which is the network floor through Cloudflare to eastasia.

### Time series (60 s buckets, all samples)

| Minute | Samples | p95 all | p95 reads | p95 writes (ms) |
|---:|---:|---:|---:|---:|
| 0 | 290 | 1,595 | 256 | 305 |
| 1 | 288 | 3,827 | 2,485 | 3,527 |
| **2** | 193 | **10,518** | **7,944** | **6,099** |
| 3 | 319 | 304 | 211 | 221 |
| 4 | 330 | 299 | 229 | 231 |
| 5 | 306 | 348 | 226 | 262 |
| 6 | 321 | 316 | 252 | 286 |
| 7 | 321 | 441 | 379 | 374 |
| 8 | 320 | 505 | 399 | 448 |
| 9 | 294 | 2,239 | 1,995 | 2,295 |
| 10 | 334 | 430 | 267 | 288 |
| 11 | 328 | 415 | 281 | 317 |
| 12 | 319 | 393 | 288 | 318 |
| 13 | 327 | 393 | 303 | 534 |
| 14 | 322 | 326 | 249 | 285 |

Of the 44 samples slower than 5 s, 43 fall between t=60 s and t=180 s, on every endpoint, while the first 10 doctors were starting their first cycles. After t=180 s the run holds a steady p95 of about 300-450 ms, apart from a one-minute blip at minute 9 that recovers in the next bucket.

### Server side (Azure Monitor, 1-minute maxima over the run)

| Resource | CPU | Memory | Other |
|---|---|---|---|
| swiftcare-gateway | 1-7% of 0.25 vCPU | 106 MiB | 0 restarts |
| swiftcare-auth | 0-40% (login bursts only) | 142 MiB | 0 restarts |
| swiftcare-patient | 0-2% | 139 MiB | 0 restarts |
| swiftcare-queue | 2-9% | 189 MiB | 0 restarts |
| swiftcare-medical-record | 0-15% | 112 MiB | 0 restarts |
| swiftcare-prescription | 0-20% | 138 MiB | 0 restarts |
| swiftcare-mysql (B1ms) | 16-19% | 23-25% | 9-16 active connections |

Every app and the database stayed far below their limits throughout, including during the minute 1-2 slowdown. That slowdown was therefore not a resource running out. The Sprint 1 Azure round saw the same settling phase of about three minutes (`RESULT-load-azure-20260907.md`). From client timings and platform metrics alone, its cause cannot be pinned down between the ingress, connection-pool growth on the first concurrent cycles, and the internet and Cloudflare path. The Container Apps log workspace returned no rows for the run window, so service logs could not be checked either.

## Verdict - against TEST-PLAN-AZURE.md section 12.5

| Criterion | Threshold | Plan window (t ≥ 60 s) | Settled window (t ≥ 180 s) |
|---|---|---:|---:|
| Reads p95 | 1,200 ms or less | 704 ms PASS | 353 ms PASS |
| Reads p99 | 2,000 ms or less | 4,277 ms **FAIL** | 1,553 ms PASS |
| Writes p95 | 2,000 ms or less | 1,202 ms PASS | 482 ms PASS |
| Writes p99 | 3,500 ms or less | 3,527 ms **FAIL** | 2,008 ms PASS |
| Error rate, response code | 0.5% or less | 0.07% PASS | 0.00% PASS |
| Latency drift | within 20% | 0.10 / 0.13, falling (settling, not degradation) | 1.22 / 1.22, **just outside** |

**Deviation, as in the Sprint 1 round.** The settled window drops the first 180 s, the same documented deviation `RESULT-load-azure-20260907.md` applied to its Azure settling phase. It is reported next to the plan's own window, not instead of it.

**Overall:**

- **Settled window: 5 of 6 pass.** Latency and errors are well inside every limit. Drift exceeds its allowance by 2 percentage points: last-third p95 about 280-320 ms against 230-260 ms.
- **Plan window: fails both p99s**, because of the three-minute settling phase. Unlike the Sprint 2 round, which had no settling phase after a warm-up, this run settled even though it was warmed.
- **Throughout:** no endpoint failed functionally, and every Sprint 3 write completed.

## Comparison

| | Local SWC-126 (same workload) | Azure Sprint 2 (SWC-88) | Azure Sprint 3 (this run) |
|---|---|---|---|
| Users / duration | 20 / 900 s | 20 / 900 s | 20 / 900 s |
| Samples / throughput | 5,161 / 5.7 req/s | 2,762 / 3.1 req/s | 4,612 / 5.1 req/s |
| Reads p95 / p99 | 14 / 22 ms | 401 / 557 ms | 353 / 1,553 ms settled |
| Writes p95 / p99 | 42 / 54 ms | 432 / 577 ms | 482 / 2,008 ms settled |
| Error rate | 0.00% | 0.153% (seed-data 409s) | 0.00% settled, 0.07% plan window |
| Drift | 1.00 | 1.00 | 1.22 settled |

- **Against local:** the p50 is about 150 ms higher than local, almost exactly the 106 ms network floor plus the 0.25 vCPU replicas. That is the network-inclusive, deployed-environment cost, not a code difference.
- **Against Sprint 2:** Sprint 3 p95 is in the same range as the Sprint 2 queue endpoints (353 against 401 ms for reads, 482 against 432 ms for writes). The p99 is higher, carried by occasional 1.5-5 s outliers on the single small replicas.
- **Cloudflare:** it now sits in the path, and the Sprint 2 baseline may not have had it, so part of any difference may be the extra hop.

## Environment findings

1. **SWC-128 also occurs on Azure, at clinic-peak load.** Three `PUT /api/queue/call-next` requests returned 500: one at t=97 s, then two within 25 ms of each other at t=168 s from doctors 8 and 10. Two concurrent call-next requests failing together is the deadlock signature recorded locally as SWC-128. Locally it needed Stress concurrency. On Azure it appeared with only 10 doctors, during the slow phase, when transactions stayed open longer. The Azure logs returned no rows to confirm the exception text. This strengthens the case for the SWC-128 fix in Sprint 4.
2. **Cloudflare rate limiting blocks bursts of logins from one IP.** The doctor reset after the first Smoke attempt was stopped with `error code: 1015` at the seventh login within a few seconds. A clinic where staff sign in together at the start of a shift, behind one office IP, could hit the same block. The rule was disabled for this test and should be reviewed before real use.
3. **Login takes 1.5-2.7 s** on the 0.25 vCPU AuthService replica (BCrypt). It is setup here, but users feel it.
4. **Settling phase.** A slow phase of about three minutes after load starts, with idle platform metrics, was seen in the Sprint 1 round and again here. It deserves an ingress and connection-level investigation if the deployment is to serve a real clinic.

## Data footprint

Seeding and the warm-up, Smoke and Load runs added to the deployed databases:

- 10 Doctor and 10 Receptionist accounts;
- 200 patients and queue entries;
- about 145 consultations with vital signs, about 145 completed queue entries, and about 140 prescriptions, most of them dispensed.

The last Load cycles left a few consultations and prescriptions open until the next `-QueueVolume 0` reset. All of it is synthetic. Whether it stays is agreed with DevOps.
