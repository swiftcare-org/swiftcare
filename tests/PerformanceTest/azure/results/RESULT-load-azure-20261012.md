# Performance run report - LOAD (Azure, Sprint 4)

**Jira:** SWC-137  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 13

This is the SWC-137 Load result: the first complete Load run after a passing Smoke gate ([`RESULT-smoke-azure-20261012.md`](RESULT-smoke-azure-20261012.md)), judged against the section 13.5 criteria on the plan's own measurement window.

## Headline

**PASS, 6 of 6 criteria, on the plan's measurement window with no deviation.** 20 concurrent users across the six Sprint 4 groups (5 Doctor workflow, 4 Receptionist counter, 4 Activity Feed, 4 Patient History, 2 Admin Reports, 1 Admin Operations), **30 s ramp-up, 900 s run**, **4,803 samples at 5.3 req/s**, **0 errors**. In 15 minutes, 70 patients were called and completed, 70 prescriptions written, 65 dispensed and 58 target accounts updated.

| Criterion (t >= 60 s) | Measured | Limit | Result |
|---|---:|---:|:--:|
| Reads p95 / p99 | **300 / 611 ms** | 1,200 / 2,000 ms | PASS |
| Writes p95 / p99 | **286 / 419 ms** | 2,000 / 3,500 ms | PASS |
| Error rate | **0.00%** (0 of 3,757) | 0.5% | PASS |
| Latency drift (last / first third p95) | **1.10** reads, **1.12** writes | within 20% | PASS |

No sample of the whole run took longer than 5 s, and no Duration Assertion fired.

## Run metadata

| Field | Value |
|---|---|
| Run type | Load |
| Date / time | 2026-10-12 18:44:40 to 18:59:40 IST (13:14:40 to 13:29:40 UTC) |
| JMeter version | 5.6.3, CLI (non-GUI), on Temurin 21.0.12.1 |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -q data/sprint4-run.properties -Jhost=api.swiftcare.me -Jthreads=0 -JusersClinicDoctor=5 -JusersClinicCounter=4 -JusersActivity=4 -JusersHistory=4 -JusersReports=2 -JusersAdmin=1 -Jrampup=30 -Jduration=900 -l results/load-swc137.jtl -e -o results/load-swc137-report` |
| Target | `https://api.swiftcare.me`, through Cloudflare, to the development environment's Container Apps (eastasia) |
| Threads / ramp / duration | 5 + 4 + 4 + 4 + 2 + 1 = 20 / 30 s / 900 s |
| Pacing | Doctor 1-3 s between steps, 40-50 s before the next patient; counter queue and pending list every 4.5-5.5 s, dispense 30 s or more after a save; Activity Feed every 10 s, Patient History every 8 s, Reports every 30 s, Admin Operations every 15 s |
| Deployment | Current `develop`, deployed by CD on 2026-10-12. Every app 0.25 vCPU / 0.5 GiB, one replica; MySQL flexible server `swiftcare-mysql`. |
| Preconditions | 20 consecutive `GET /health` requests answered 200 in under 1 s; warm-up and Smoke gate passed |
| Seeded data | `seed-sprint4-azure.ps1`, 150 patients checked in on 2026-10-12; doctors reset with `-QueueVolume 0` after the warm-up and after Smoke |
| Cloudflare | Rate-limiting rule disabled by DevOps for the test window, to be re-enabled after the run |
| Report | `results/load-swc137-report/index.html` |

### Target host

SWC-161 is moving development to `api-dev.swiftcare.me` and giving `api.swiftcare.me` to production. Before seeding, the following was checked from the load generator, and `api.swiftcare.me` was used as development, the same host as SWC-127:

- `api-dev.swiftcare.me` resolves to the development gateway `swiftcare-gateway`, but its TLS handshake fails: the hostname is not yet bound to the app.
- The production gateway `swiftcare-prod-gateway` answered 404 at its default domain: no production gateway is serving.
- The `azure-development` GitHub Environment still names `https://swiftcare.me` as the development frontend.
- The development gateway's default domain answered `/health` with 200.

## Results

Reads and writes are classified as in section 13.2. The Sprint 2 endpoints the clinical workflow depends on are reported separately. The 20 logins are setup (0.8 to 5.2 s) and are excluded.

### Per endpoint, plan window (t >= 60 s, 840 s, 4,508 samples)

| Class | Endpoint | n | Errors | p95 | p99 | max (ms) |
|---|---|---:|---:|---:|---:|---:|
| Read | GET /api/notifications/today | 321 | 0 | 332 | 651 | 887 |
| Read | GET /api/notifications | 321 | 0 | 289 | 524 | 845 |
| Read | GET /api/patients/{id} | 382 | 0 | 311 | 711 | 1,520 |
| Read | GET /api/consultations/patient/{id} | 383 | 0 | 274 | 530 | 1,537 |
| Read | GET /api/vitals/patient/{id} | 383 | 0 | 282 | 806 | 1,490 |
| Read | GET /api/prescriptions/patient/{id} | 448 | 0 | 278 | 629 | 1,418 |
| Read | GET /api/reports/daily | 55 | 0 | 431 | 748 | 748 |
| Read | GET /api/reports/monthly | 55 | 0 | 304 | 594 | 594 |
| Read | GET /api/prescriptions/report/daily | 55 | 0 | 262 | 282 | 282 |
| Read | GET /api/prescriptions/report/monthly | 55 | 0 | 252 | 383 | 383 |
| Read | GET /api/users | 54 | 0 | 530 | 957 | 957 |
| Read | GET /api/audit-logs | 54 | 0 | 311 | 611 | 611 |
| Read | GET /api/consultations/patient/{id}/latest-follow-up | 65 | 0 | 243 | 547 | 547 |
| Read | GET /api/consultations/latest-completed | 65 | 0 | 232 | 334 | 334 |
| Read | GET /api/prescriptions/pending | 555 | 0 | 281 | 451 | 1,546 |
| Read | GET /api/prescriptions/queue/{queueId} | 63 | 0 | 309 | 369 | 369 |
| Write | PUT /api/users/{id} | 54 | 0 | 283 | 327 | 327 |
| Write | POST /api/consultations/{id}/vitals | 65 | 0 | 255 | 312 | 312 |
| Write | POST /api/consultations/{id}/complete | 65 | 0 | 304 | 343 | 343 |
| Write | POST /api/prescriptions | 65 | 0 | 288 | 455 | 455 |
| Write | POST /api/prescriptions/{id}/items | 65 | 0 | 283 | 312 | 312 |
| Write | DELETE /api/prescriptions/{id}/items/{medicineId} | 65 | 0 | 279 | 419 | 419 |
| Write | PUT /api/prescriptions/{id}/dispense | 64 | 0 | 386 | 1,444 | 1,444 |
| Sprint 2 | GET /api/queue/today | 556 | 0 | 436 | 683 | 1,606 |
| Sprint 2 | GET /api/queue/today/current | 65 | 0 | 508 | 3,124 | 3,124 |
| Sprint 2 | PUT /api/queue/call-next | 65 | 0 | 486 | 1,061 | 1,061 |
| Sprint 2 | POST /api/consultations | 65 | 0 | 328 | 470 | 470 |

Every sample returned its expected status.

### By class

| Window | Class | Samples | p50 | p95 | p99 | max (ms) | Drift |
|---|---|---:|---:|---:|---:|---:|---:|
| Plan (t >= 60 s) | Reads | 3,314 | 168 | 300 | 611 | 1,546 | 262 → 289 ms (1.10) |
| Plan (t >= 60 s) | Writes | 443 | 184 | 286 | 419 | 1,444 | 279 → 313 ms (1.12) |

The whole run including ramp-up, excluding logins: 4,783 samples, 0 errors, p50 180 ms, p95 350 ms, p99 677 ms. The fastest sample was 121 ms, the network floor through Cloudflare to eastasia.

### Time series (60 s buckets, excluding logins)

| Minute | Samples | p95 all | p95 reads | p95 writes | max (ms) |
|---:|---:|---:|---:|---:|---:|
| 0 | 275 | 528 | 491 | 837 | 1,246 |
| 1 | 325 | 334 | 334 | 293 | 483 |
| 2 | 326 | 312 | 313 | 249 | 600 |
| 3 | 331 | 317 | 317 | 279 | 440 |
| 4 | 325 | 310 | 312 | 278 | 426 |
| 5 | 323 | 314 | 317 | 271 | 461 |
| 6 | 324 | 342 | 350 | 253 | 594 |
| 7 | 302 | 446 | 470 | 389 | 1,324 |
| 8 | 314 | 341 | 343 | 291 | 524 |
| 9 | 312 | 1,061 | 1,153 | 1,061 | 3,124 |
| 10 | 323 | 330 | 330 | 279 | 845 |
| 11 | 325 | 325 | 325 | 264 | 416 |
| 12 | 324 | 313 | 315 | 234 | 404 |
| 13 | 331 | 451 | 451 | 419 | 1,289 |
| 14 | 323 | 357 | 360 | 343 | 548 |

Flat at about 310-360 ms p95 after the first minute, with three single-minute blips (minutes 7, 9 and 13):

- **Minute 9 (about 18:54:33 IST)** is the largest. Its slow samples fall within about 5 seconds (t = 592.9 to 597.4 s, with two follow-on calls at 599 s): 16 requests at 1.0 to 3.1 s across the patient, medical-record, prescription and queue services at once. The two slowest spent 2.2 s and 1.3 s only establishing the TCP connection, before any request reached a service.
- **Minutes 7 and 13** each contain one slow sample (`GET /api/queue/today`, 1.3 s), with 0.6 and 0.9 s spent opening the connection.

Azure Monitor shows no app or database near its limit at any minute. A short stall shared across unrelated services, with connection setup accounting for the worst cases and idle back ends, points to the network path (the load generator's connection, Cloudflare or ingress) rather than to any service. Its exact cause cannot be proven from client-side timings. None of the blips affects a criterion.

### Server side (Azure Monitor, 1-minute maxima, 13:14 to 13:31 UTC)

| Resource | CPU (of its allocation) | Memory | Other |
|---|---|---|---|
| swiftcare-gateway | 9.6% of 0.25 vCPU | 112 MiB | 0 restarts |
| swiftcare-auth | 48.9% (the 20 setup logins) | 154 MiB | 0 restarts |
| swiftcare-patient | 3.5% | 202 MiB | 0 restarts |
| swiftcare-queue | 5.8% | 184 MiB | 0 restarts |
| swiftcare-medical-record | 5.5% | 128 MiB | 0 restarts |
| swiftcare-prescription | 10.6% | 155 MiB | 0 restarts |
| swiftcare-notification | 6.0% | 169 MiB | 0 restarts |
| swiftcare-mysql | 17.4% | 21.2% | 20 active connections at peak |
| swiftcare-messaging (Kafka) | 18 millicores | 451 MiB | |

Every app stayed at or below 11% of its 0.25 vCPU apart from AuthService's login burst, every app used well under half of its 0.5 GiB, and no app restarted. The platform was far from its limits throughout.

## Verdict - against TEST-PLAN-AZURE.md section 13.5

| Criterion | Threshold | Actual (plan window) | Margin | Result |
|---|---|---:|---:|:--:|
| Reads p95 | 1,200 ms or less | 300 ms | 4.0x | PASS |
| Reads p99 | 2,000 ms or less | 611 ms | 3.3x | PASS |
| Writes p95 | 2,000 ms or less | 286 ms | 7.0x | PASS |
| Writes p99 | 3,500 ms or less | 419 ms | 8.4x | PASS |
| Error rate | 0.5% or less | 0.00% | - | PASS |
| Latency drift | within 20% | 1.10 reads / 1.12 writes | - | PASS |

**Overall: PASS, 6 of 6**, on the plan's standard window as written. Every Sprint 4 read and write, and every Sprint 3 workflow step running alongside them, completed.

## Comparison

| | Local, same 20-user profile (SWC-137 validation) | Azure Sprint 3 (SWC-127) | Azure Sprint 4 (this run) |
|---|---|---|---|
| Workload | six Sprint 4 groups, 5:4:4:4:2:1 | Doctor 10 + Counter 10 | six Sprint 4 groups, 5:4:4:4:2:1 |
| Users / duration | 20 / 900 s | 20 / 900 s | 20 / 900 s |
| Samples / throughput | 5,131 / 5.7 req/s | 4,915 / 5.5 req/s | 4,803 / 5.3 req/s |
| Reads p95 / p99 | 26 / 34 ms | 193 / 255 ms | 300 / 611 ms |
| Writes p95 / p99 | 57 / 77 ms | 239 / 294 ms | 286 / 419 ms |
| Error rate | 0.00% | 0.00% | 0.00% |
| Drift | 1.25 / 1.20 | 0.96 / 0.99 | 1.10 / 1.12 |
| MySQL CPU peak | - | 15-17% | 17.4% |

- **Against local:** the Azure p50 of about 170-185 ms against 16-39 ms locally is the network floor (121 ms minimum through Cloudflare) plus the 0.25 vCPU replicas. Endpoint by endpoint, the slowest locally are also among the slowest on Azure (`GET /api/users`, the daily report, `notifications/today`), so the relative cost per endpoint carries over.
- **Against Sprint 3 Azure:** throughput is the same at the same concurrency, and the shared Sprint 3 write steps are close (writes p95 239 → 286 ms). Reads are slower at p95 (193 → 300 ms) and p99 (255 → 611 ms). The read mix now includes the Sprint 4 endpoints with larger payloads (`GET /api/users` 26.8 KB, notifications 13.1 KB, audit log 11.4 KB) and the minute-9 stall, which accounts for 23 of the slowest 34 reads (the slowest 1%). Server-side load is unchanged: MySQL 17% in both, every app far from its CPU cap.

## Findings

1. **The deployed Sprint 4 system meets every Azure criterion at the modelled clinic peak**, with 3-8x margin on latency and no errors, including the newly deployed NotificationService.
2. **The platform is idle at 20 users.** No app exceeded 11% of its 0.25 vCPU outside the login burst, MySQL peaked at 17% CPU and 20 connections, and nothing restarted. The local SWC-136 Stress run located the first real limit (MySQL connections) at about 126 users.
3. **Measure from a stable connection.** As in SWC-127, the remaining latency blips are spread across unrelated services while the back ends are idle, which points to the network path. The `/health` connection check remains a precondition for every Azure round.
4. **Logins are slow on the deployed AuthService** (up to 5.4 s when several arrive together): BCrypt on 0.25 vCPU. Staff sign in once per shift, so it does not affect the workflow, but a burst of simultaneous logins at the start of a clinic day would queue.
5. **`GET /api/users` returns the full user list (26.8 KB here)** and is the slowest Sprint 4 read at p95 530 ms. It grows with every account, including test accounts left by these runs.

## Data footprint

The seed, warm-up, Smoke and Load of this round added to the deployed development databases:

- 4 Admin and 4 target accounts, alongside the reused SWC-127 Doctor and Receptionist pools;
- 150 patients and queue entries;
- about 80 consultations with vital signs, completed queue entries and prescriptions, about 70 of them dispensed;
- the notifications those events publish, and audit-log entries for about 65 target-account updates.

All of it is synthetic. There is no reset equivalent on Azure.

## Post-run actions

- Cloudflare rate-limiting rule re-enabled: _record time and by whom_.
- Environment shut down per `deployment/terraform/README.md`, "Cost controls", unless DevOps keeps it running: _record_.
