# SWC-136 — Sprint 4 local Stress result

## Outcome

- **Breaking point:** **~110–126 active users** (t = 180–240 s), reached on the error-rate
  and throughput triggers together. The p95 trigger was reached later, at ~200–231 users.
- **Cause:** MySQL connection exhaustion. `max_connections` is the MySQL 8.4 default of 151;
  `Max_used_connections` reached 152 at 12:56:22 (t = 180 s) and stayed pinned for the rest
  of the run. MySQL refused 10,359 connection attempts (`Connection_errors_max_connections`).
- **First services under pressure:** AuthService and QueueService reached their 1.0 CPU caps
  at 12:55:50 (t = 148 s), while latency was rising but before any errors. MySQL reached its
  2.0 CPU cap and its connection limit at t = 180–184 s, exactly where errors began.
- **Integrity:** all 15 post-run checks returned zero violations. No duplicate consultation,
  prescription, notification or doctor/room assignment was produced under 300 users, so the
  SWC-128 Call Next fix held under stress.
- **Verdict:** Stress is not a pass/fail phase (`TEST-PLAN.md` §10.2). This run supersedes the
  **BLOCKED** status in `RESULT-SWC-136-20261012.md` for the Stress phase.

## Environment and execution

| Item | Value |
| --- | --- |
| Date / window | 2026-10-12, 12:53:22–13:03:22 IST (07:23:22–07:33:22 UTC) |
| Branch / commit | `test/SWC-136-sprint4-performance` at `e154013` |
| Host | Windows 11, 22 logical CPUs, 15.37 GiB physical memory (a different host from the Smoke/Load runs) |
| Docker | Docker 29.8.0, 22 CPUs and 7.44 GiB to the Docker VM |
| Load generator | JMeter 5.6.3 on Temurin 21.0.12.1 (portable, via `JM_LAUNCH`), same host as the system under test |
| Stack | Compose project `swiftcare-swc136`, Gateway `http://127.0.0.1:8016`, `docker-compose.swc136.yml` + `docker-compose.perf.yml` |
| Resource caps | mysql 2.0 CPU / 1 GiB; Gateway and each service 1.0 CPU / 512 MiB |
| Data | Fresh volumes, fresh migrations, `seed-sprint4.ps1 -DoctorCount 75 -ReceptionistCount 60 -AdminCount 30 -QueueVolume 6000` |
| Isolation | No shared Gateway (port 8000) or Azure endpoint was targeted; `kafka-ui` stopped |

Profile, as registered in `TEST-PLAN.md` §10.1: one continuous linear ramp from 0 to 300 users
over 600 s — 75 Doctor, 60 Counter, 60 Activity Feed, 60 History, 30 Reports, 15 Admin — with
Sprint 3 clinical pacing and Sprint 4 group delays (300/500/1000/1000 ms). The full ramp
completed; there was no safety abort.

Host free memory was not captured as a pre-run gate value on this host. The run was started at
the tester's direction after the earlier gate concern; no host instability was observed.

## Time series (30-second windows, READ + WRITE samples)

Functional errors follow `summarize-sprint4-results.ps1`: non-2xx or a non-duration assertion
failure. Throughput counts all completed samples, including fast failures.

| t (s) | Active | Samples | Req/s | p95 (ms) | p99 (ms) | Errors | Error % |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0–30 | 19 | 1,333 | 44.4 | 46 | 157 | 0 | 0.00 |
| 30–60 | 35 | 3,240 | 108.0 | 58 | 102 | 0 | 0.00 |
| 60–90 | 50 | 4,859 | 162.0 | 86 | 601 | 0 | 0.00 |
| 90–120 | 66 | 6,367 | 212.2 | 120 | 514 | 0 | 0.00 |
| 120–150 | 79 | 7,047 | 234.9 | 398 | 756 | 0 | 0.00 |
| 150–180 | 95 | 7,625 | 254.2 | 523 | 913 | 1 | 0.01 |
| 180–210 | 110 | 8,385 | 279.5 | 688 | 1,242 | 32 | 0.38 |
| **210–240** | **126** | 8,918 | **297.3** | 512 | 1,307 | 108 | **1.21** |
| **240–270** | **139** | 8,571 | **285.7** | 978 | 1,552 | 379 | **4.42** |
| 270–300 | 155 | 8,852 | 295.1 | 985 | 1,835 | 858 | 9.69 |
| 300–330 | 170 | 9,153 | 305.1 | 1,127 | 2,987 | 1,299 | 14.19 |
| 330–360 | 186 | 9,387 | 312.9 | 1,233 | 3,615 | 1,488 | 15.85 |
| 360–390 | 200 | 10,117 | 337.2 | 1,199 | 2,395 | 2,109 | 20.85 |
| 390–420 | 226 | 6,316 | 210.5 | 5,617 | 9,331 | 2,197 | 34.79 |
| 420–450 | 231 | 884 | 29.5 | 9,013 | 9,653 | 884 | 100.00 |
| 450–480 | 246 | 3,946 | 131.5 | 7,231 | 8,996 | 2,630 | 66.65 |
| 480–510 | 260 | 11,038 | 367.9 | 1,211 | 1,795 | 5,832 | 52.84 |
| 510–540 | 275 | 11,269 | 375.6 | 1,265 | 1,788 | 6,484 | 57.54 |
| 540–570 | 290 | 11,883 | 396.1 | 1,246 | 1,707 | 6,880 | 57.90 |
| 570–600 | 300 | 11,689 | 389.6 | 1,416 | 1,996 | 7,157 | 61.23 |

### Breaking-point triggers (`TEST-PLAN.md` §10.2)

| Trigger (sustained 60 s) | First reached | Active users |
| --- | --- | ---: |
| Aggregate functional error rate > 1% | 210–270 s (1.21%, 4.42%) | 126–139 |
| Throughput flat/falling while threads rise | 210–300 s (297 → 286 → 295 req/s) | 126–155 |
| Aggregate p95 > 2000 ms | 390–450 s (5,617, 9,013 ms) | 226–231 |

The knee is therefore **~126 active users**, with first errors from 110. Throughput rises again
after 480 s only because most requests fail fast (see below); it is not recovery.

The run passed through three regimes: healthy scaling to ~95 users; connection-limited
degradation from ~110 users; a near-outage at 390–480 s (29.5 req/s and 100% errors at
420–450 s); then a fast-fail regime at ~53–61% errors to the end. No container restarted and
every container stayed healthy.

## Saturation evidence

Monitor sampling interval ≈ 12 s (52 samples per container).
`max_connections = 151`, `Max_used_connections = 152` at 07:26:22 UTC (12:56:22 IST, t = 180 s).

| t (s) | Event |
| ---: | --- |
| 148 | AuthService (99%) and QueueService (97%) reach their 1.0 CPU caps; p95 already up from 120 to 398–523 ms |
| 180 | MySQL `Max_used_connections` reaches 152 |
| 181 | First `Too many connections` in QueueService |
| 184 | MySQL reaches its 2.0 CPU cap (199%); monitor shows 152 connected |
| 180–210 | First functional errors (32) |
| 202 | AuthService and NotificationService begin receiving `Too many connections` |
| 280 | Setup logins begin failing (HTTP 500 and socket timeouts) |
| 282 | First HTTP 401s, on threads with no token |

| Container | CPU peak | Samples at cap | Memory peak |
| --- | ---: | ---: | ---: |
| mysql | 203% (cap 200%) | 34 / 52 | 640 MiB / 1 GiB |
| authservice | 106% | 30 / 52 | 208 MiB / 512 MiB |
| queueservice | 105% | 25 / 52 | 222 MiB / 512 MiB |
| patientservice | 127% | 1 / 52 | 117 MiB / 512 MiB |
| apigateway | 108% | 3 / 52 | 117 MiB / 512 MiB |
| prescriptionservice | 77% | 0 / 52 | 120 MiB / 512 MiB |
| notificationservice | 70% | 0 / 52 | 143 MiB / 512 MiB |
| medicalrecordservice | 17% | 0 / 52 | 86 MiB / 512 MiB |
| kafka (uncapped) | 415% | — | 743 MiB |

MySQL `threads_connected` held at 152 from t ≈ 184 s, while sampled `threads_running` stayed
between 2 and 15, so most connections were held open rather than executing. The monitor's own
connection was refused in 16 of its samples. After traffic ended, 150 connections were still
open. Memory was never a constraint.

`Too many connections` count per service log (`docker logs`, exact match):

| Service | Count | First (UTC) | Last (UTC) |
| --- | ---: | --- | --- |
| AuthService | 19,836 | 07:26:44 | 07:33:23 |
| QueueService | 9,174 | 07:26:23 | 07:33:23 |
| NotificationService | 1,491 | 07:26:44 | 07:32:38 |
| MedicalRecordService | 24 | 07:27:13 | 07:29:38 |
| PatientService | 3 | 07:27:13 | 07:27:13 |
| PrescriptionService | 0 | — | — |

**First saturated service:** AuthService and QueueService on CPU (tied, same sample, t = 148 s),
aligned with the first latency rise. **Breaking-point cause:** the shared MySQL connection limit
(with MySQL also at its CPU cap), aligned with the first errors at t = 180–210 s.

## Error analysis

38,338 of 150,879 measured samples failed. In the order the classes appear:

1. **HTTP 500 from QueueService** (from 179 s) — `GET /api/queue/today` and
   `GET /api/queue/today/current`, consistent with QueueService being the first service refused
   a MySQL connection.
2. **HTTP 503 Service Unavailable** (from 200 s) — `queue/today/current`, `notifications`,
   `notifications/today` and `prescriptions/pending` returned by the Gateway path while
   downstream services were unavailable.
3. **HTTP 401 Unauthorized** (from 282 s) — the largest class. It is a consequence, not an
   authentication defect: threads whose once-per-thread setup login failed (at least 25 of 300
   logins: 14 × HTTP 500, 11 × socket timeout, from t = 280 s, mostly Doctor Patient History,
   Activity Feed, Admin Reports and Admin Operations threads) issued every later request with no
   token. The four History endpoints each show exactly 3,005 401s from t = 282 s, matching this
   pattern. JMeter models login once per thread (A3), so these threads never re-authenticate.

Most affected endpoints: `PUT /api/queue/call-next` 52.6% errors, `GET /api/queue/today/current`
45.0%, `GET /api/queue/today` 35.3%, Admin `audit-logs`/`users` ~33–34%. Clinical writes were
least affected (7.5–14%). Per-endpoint p95 before 210 s and after 240 s is in
`stress-SWC-136-per-label.csv`.

## Whole-run threshold summary (reference only)

After the pre-registered 90-second exclusion: 141,447 samples. The Load thresholds do not
decide Stress; they are reported for completeness.

| Class | Samples | p95 | p99 | Error rate | First-third p95 | Last-third p95 | Drift |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Read | 135,699 | 1,264 ms | 5,520 ms | 27.06% | 506 ms | 1,293 ms | 155.53% |
| Write | 5,748 | 1,310 ms | 3,599 ms | 28.06% | 492 ms | 1,794 ms | 264.63% |

Duration-assertion breaches (excluded from functional errors): 672 reads, 3 writes.

## Data integrity

`verify-sprint4-integrity.ps1` after the run: all 15 checks returned **0 violations**
(`stress-SWC-136-integrity.csv`). Errors under stress were refusals and failed requests, not
inconsistent writes.

## Comparison with Sprint 3

Sprint 3 SWC-126 inferred its knee at 46–62 active users with two groups (Doctor, Receptionist),
where QueueService CPU saturated first and call-next deadlocked (SWC-128). Sprint 4 reaches
~126 users with six groups. The mixes differ — four of the six Sprint 4 groups are lighter
read-heavy roles — so this is not a like-for-like capacity gain, but call-next no longer returns
deadlock 500s and the limiting factor has moved from QueueService CPU to the shared MySQL
connection limit.

## Recommendations

1. **Size connection pools against `max_connections`.** Six services share one MySQL with a
   limit of 151, and each MySqlConnector pool defaults to 100. Set an explicit
   `Maximum Pool Size` per service so the sum stays below the limit with admin headroom, or
   raise `max_connections` deliberately in line with MySQL's memory budget. Re-run this profile
   afterwards to locate the next limit.
2. **Investigate AuthService database use per request.** It recorded 19,836 refused
   connections, far more than its 300 logins, which suggests a database lookup on routine
   requests. Confirm whether that lookup is needed per request or can be cached.
3. **QueueService load.** It was first both to hit its CPU cap and to be refused a connection;
   its full-day queue reads (noted in Sprint 3) remain the main QueueService cost.
4. Optional per `TEST-PLAN.md`: the 400-user extension is not warranted, because 300 users found
   a knee.

## Deviations and limitations

- Stress ran on a different host from the Smoke and Load runs (22 vs 16 logical CPUs, similar
  memory). Docker limits and per-service caps were identical. Smoke was not repeated on this
  host; the plan was validated with a zero-thread run on Java 21 before seeding.
- JMeter and the stack shared one host, as in Sprint 3.
- The monitor samples about every 12 s, and 16 MySQL samples are blank because MySQL refused
  the monitor's own connection.
- `stress-SWC-136-summary.json` records its JTL path as `swiftcare\results\` because the script
  resolves output paths against the process directory. The JTL read was this directory's
  `stress-SWC-136.jtl`; the summary, integrity and resource files were moved here after the run.
- The login-failure count (at least 25) is a lower bound; some JTL rows contain multi-line
  failure messages that simple parsing skips.

## Evidence

Committed: this report, `stress-SWC-136-summary.json`, `RESULT-stress-SWC-136-20261012-SHA256.txt`.
Retained locally (git-ignored): `stress-SWC-136.jtl`, `stress-SWC-136-jmeter.log`,
`stress-SWC-136-report/`, `stress-SWC-136-resources-containers.csv`,
`stress-SWC-136-resources-mysql.csv`, `stress-SWC-136-integrity.csv`,
`stress-SWC-136-windows.csv`, `stress-SWC-136-errors.csv`, `stress-SWC-136-per-label.csv`.
SHA-256 hashes for all of them are in `RESULT-stress-SWC-136-20261012-SHA256.txt`.
