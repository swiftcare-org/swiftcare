# Performance run report - SMOKE (Azure, Sprint 4)

**Jira:** SWC-137  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 13

Gate for the Load run in [`RESULT-load-azure-20261012.md`](RESULT-load-azure-20261012.md). One thread in each of the six Sprint 4 groups against the deployed development environment, to prove every sampler is wired correctly before putting 20 users on shared infrastructure.

## Run metadata

| Field | Value |
|---|---|
| Run type | Smoke (gate) |
| Date / time | 2026-10-12 18:41:35 to 18:42:36 IST |
| JMeter version | 5.6.3, CLI (non-GUI), on Temurin 21.0.12.1 |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -q data/sprint4-run.properties -Jhost=api.swiftcare.me -Jthreads=0 -JusersClinicDoctor=1 -JusersClinicCounter=1 -JusersActivity=1 -JusersHistory=1 -JusersReports=1 -JusersAdmin=1 -Jrampup=6 -Jduration=60 -JcycleDelay=5000 -JcycleRange=0 -JdispenseAfterMs=5000 -l results/smoke-swc137-3.jtl -e -o results/smoke-swc137-3-report` |
| Target | `https://api.swiftcare.me`, through Cloudflare, to the development environment's Azure Container Apps (eastasia). See the Load report for how the host was confirmed as development. |
| Deployment | Current `develop`, deployed by CD on 2026-10-12. Every app 0.25 vCPU / 0.5 GiB, one replica. |
| Connection check | 20 consecutive `GET /health` requests: all 200, 0.21 to 0.89 s |
| Warm-up before this run | `results/warmup-swc137.jtl`: 1 user per group, 60 s, 101 samples, 0 non-2xx, discarded |
| Seeded data | `seed-sprint4-azure.ps1`: 10 Doctor and 10 Receptionist accounts (reused from SWC-127), 4 Admin/target pairs, 150 patients checked in on 2026-10-12; doctors reset with `-QueueVolume 0` after the warm-up and after Smoke |
| Cloudflare | Rate-limiting rule disabled by DevOps for the test window |

## Results

111 samples over 60 s, 0 failures, no assertion failures, all 31 sampler labels present (the 17 Sprint 3 labels, one shared Sprint 4 login label and the 13 Sprint 4 samplers). Excluding the six setup logins, p50 was 204 ms, p95 495 ms and the maximum 677 ms.

| Group | Sampler | n | Codes | avg (ms) | max (ms) |
|---|---|---:|---|---:|---:|
| Activity Feed | `GET /api/notifications/today` | 6 | 200 x 6 | 305 | 677 |
| Activity Feed | `GET /api/notifications` | 6 | 200 x 6 | 303 | 402 |
| Patient History | `GET /api/patients/{id}` | 7 | 200 x 7 | 244 | 525 |
| Patient History | `GET /api/consultations/patient/{id}` | 7 | 200 x 7 | 264 | 513 |
| Patient History | `GET /api/vitals/patient/{id}` | 7 | 200 x 7 | 177 | 202 |
| Patient History | `GET /api/prescriptions/patient/{id}` | 7 | 200 x 7 | 203 | 292 |
| Admin Reports | `GET /api/reports/daily` | 2 | 200 x 2 | 224 | 265 |
| Admin Reports | `GET /api/prescriptions/report/daily` | 2 | 200 x 2 | 158 | 173 |
| Admin Reports | `GET /api/reports/monthly` | 2 | 200 x 2 | 221 | 309 |
| Admin Reports | `GET /api/prescriptions/report/monthly` | 2 | 200 x 2 | 177 | 186 |
| Admin Operations | `GET /api/users` | 4 | 200 x 4 | 238 | 281 |
| Admin Operations | `GET /api/audit-logs` | 4 | 200 x 4 | 270 | 417 |
| Admin Operations | `PUT /api/users/{id}` | 4 | 200 x 4 | 291 | 573 |
| Doctor workflow | `GET /api/queue/today/current` | 3 | 204 x 3 | 183 | 224 |
| Doctor workflow | `PUT /api/queue/call-next` | 3 | 200 x 3 | 252 | 310 |
| Doctor workflow | `GET /api/consultations/patient/{id}/latest-follow-up` | 3 | 204 x 3 | 148 | 159 |
| Doctor workflow | `POST /api/consultations` | 2 | 201 x 2 | 229 | 280 |
| Doctor workflow | `POST /api/consultations/{id}/vitals` | 2 | 201 x 2 | 231 | 259 |
| Doctor workflow | `POST /api/consultations/{id}/complete` | 2 | 200 x 2 | 165 | 165 |
| Doctor workflow | `GET /api/consultations/latest-completed` | 2 | 200 x 2 | 205 | 241 |
| Doctor workflow | `GET /api/prescriptions/patient/{id}` | 2 | 200 x 2 | 318 | 473 |
| Doctor workflow | `POST /api/prescriptions` | 2 | 201 x 2 | 281 | 357 |
| Doctor workflow | `POST /api/prescriptions/{id}/items` | 2 | 201 x 2 | 366 | 495 |
| Doctor workflow | `DELETE /api/prescriptions/{id}/items/{medicineId}` | 2 | 200 x 2 | 312 | 430 |
| Counter | `GET /api/queue/today` | 7 | 200 x 7 | 361 | 478 |
| Counter | `GET /api/prescriptions/pending` | 7 | 200 x 7 | 274 | 545 |
| Counter | `GET /api/prescriptions/queue/{queueId}` | 3 | 200 x 3 | 288 | 491 |
| Counter | `PUT /api/prescriptions/{id}/dispense` | 3 | 200 x 3 | 183 | 184 |
| Setup | logins (6: Doctor, Receptionist and 4 Sprint 4 groups) | 6 | 200 x 6 | 5,045 | 5,449 |

Logins take 4.3 to 5.4 s: BCrypt verification on the 0.25 vCPU AuthService replica, with six arriving within six seconds. Login is a once-per-thread setup step, as in every earlier round.

## Verdict

**PASS.** Every sampler returned its expected status and every body assertion held. The Sprint 4 groups ran end to end on the deployed environment: the activity feed and today's notifications from the deployed NotificationService, patient and prescription history, daily and monthly reports, and Admin user listing, audit log and target-account update. No Cloudflare challenge or block appeared. Load run authorised.
