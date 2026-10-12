# Performance run report - LOAD (SWC-126 Sprint 3 clinical flow)

## Headline

**PASS, 6 of 6 criteria.** 20 concurrent users (10 Doctor workflow + 10 Receptionist counter),
30 s ramp-up, 900 s run, 5,161 samples at 5.7 req/s, **0 errors**.

| | Measured | Limit |
|---|---:|---:|
| Reads p95 / p99 | **14 / 22 ms** | 800 / 1500 ms |
| Writes p95 / p99 | **42 / 54 ms** | 1500 / 3000 ms |
| Error rate | **0.00%** | 0.5% |
| Latency drift (last-third / first-third p95) | **1.00** reads, **1.00** writes | within 20% |

145 consultations run end to end (call, consultation, vitals, complete, prescription, add and
remove a medicine) and 137 prescriptions dispensed at the counter, in 15 minutes.

## Run metadata

| Field | Value |
|---|---|
| Run type | Load |
| Jira | SWC-126 (stories SWC-25, SWC-26, SWC-28, SWC-29, SWC-30, SWC-38, SWC-40, SWC-41) |
| Date / time | 2026-09-27 16:36:13 to 16:51:14 IST |
| JMeter version | 5.6.3, CLI (non-GUI), on Java 21.0.12 |
| Command used | `jmeter -n -t SWC-126-clinical-flow.jmx -q user.properties -JusersDoctor=10 -JusersCounter=10 -JrampUp=30 -Jduration=900 -l results/load-SWC-126.jtl -e -o results/load-SWC-126-report` |
| Threads / ramp / duration | 10 Doctor + 10 Receptionist = 20 / 30 s / 900 s (30 s ramp, 870 s at full concurrency) |
| Pacing | Doctor: 1-3 s between steps, 40-50 s before the next patient (about one consultation per minute per doctor). Receptionist: queue and pending list every 4.5-5.5 s, dispense 30 s or more after a prescription is saved. |
| Resource limits applied? | none (plain `docker compose up -d`) |
| Machine | Windows 11, Docker Desktop (7.44 GiB to the Docker VM), JMeter on the same machine |
| Data | Fresh stack (`docker compose down -v`, migrations applied), then `seed-clinical-flow.ps1` defaults: 10 Doctor accounts with one room each, 10 Receptionist accounts, 300 patients checked in to today's waiting queue |
| Smoke gate | PASS: 1 Doctor + 1 Receptionist, 60 s, 55 samples, 0 failures, all 17 sample labels present |
| Report | `results/load-SWC-126-report/index.html` |

## Results

Measurement window: the 30 s ramp-up and the first 30 s of steady state are discarded, leaving
840 s and 4,853 samples. Reads and writes follow `TEST-PLAN.md`: Sprint 3 endpoints only. The
Sprint 2 endpoints the workflow depends on are reported separately and are not part of the
pass/fail.

### By class (measurement window)

| Class | Samples | Errors | avg | p50 | p95 | p99 | max (ms) |
|---|---:|---:|---:|---:|---:|---:|---:|
| Reads (Sprint 3) | 2,093 | 0 | 10 | 9 | 14 | 22 | 95 |
| Writes (Sprint 3) | 795 | 0 | 30 | 28 | 42 | 54 | 266 |
| All samples | 4,853 | 0 | 17 | 14 | 35 | 45 | 266 |

Whole run including ramp-up: 5,161 samples, 0 errors, p95 36 ms, p99 52 ms, max 384 ms, 5.7 req/s.

### Per endpoint (measurement window)

| Class | Label | Samples | Error % | avg | p50 | p95 | p99 | max (ms) |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| Read | GET /api/prescriptions/pending | 1,561 | 0.00 | 10 | 9 | 13 | 18 | 95 |
| Read | GET /api/consultations/patient/{id}/latest-follow-up | 135 | 0.00 | 10 | 9 | 15 | 37 | 39 |
| Read | GET /api/prescriptions/queue/{queueId} | 133 | 0.00 | 11 | 11 | 17 | 18 | 21 |
| Read | GET /api/consultations/latest-completed | 132 | 0.00 | 9 | 9 | 15 | 18 | 20 |
| Read | GET /api/prescriptions/patient/{id} | 132 | 0.00 | 11 | 10 | 20 | 40 | 55 |
| Write | POST /api/consultations/{id}/vitals | 133 | 0.00 | 23 | 23 | 33 | 39 | 114 |
| Write | POST /api/consultations/{id}/complete | 132 | 0.00 | 36 | 37 | 46 | 53 | 60 |
| Write | POST /api/prescriptions | 132 | 0.00 | 32 | 30 | 40 | 52 | 266 |
| Write | POST /api/prescriptions/{id}/items | 132 | 0.00 | 28 | 27 | 39 | 45 | 54 |
| Write | DELETE /api/prescriptions/{id}/items/{medicineId} | 132 | 0.00 | 30 | 27 | 37 | 182 | 245 |
| Write | PUT /api/prescriptions/{id}/dispense | 134 | 0.00 | 29 | 28 | 39 | 56 | 258 |
| Sprint 2 | GET /api/queue/today | 1,561 | 0.00 | 17 | 16 | 29 | 41 | 73 |
| Sprint 2 | GET /api/queue/today/current | 135 | 0.00 | 14 | 14 | 20 | 25 | 51 |
| Sprint 2 | PUT /api/queue/call-next | 135 | 0.00 | 35 | 35 | 45 | 56 | 61 |
| Sprint 2 | POST /api/consultations | 134 | 0.00 | 28 | 25 | 39 | 63 | 235 |

The 20 logins (one per thread, setup) all returned 200 and are excluded, as in every earlier
run (`TEST-PLAN.md` A3).

### Latency drift

| Class | First-third p95 | Last-third p95 | Ratio |
|---|---:|---:|---:|
| Reads | 14 ms (698 samples) | 14 ms (698 samples) | 1.00 |
| Writes | 41 ms (263 samples) | 41 ms (265 samples) | 1.00 |

### Server-side (`docker stats`, sampled every ~55 s)

| Container | CPU % during the run | Memory |
|---|---|---|
| prescriptionservice | 0.1 - 6.5% | 98 - 140 MiB |
| queueservice | 0.4 - 6.6% | 121 - 135 MiB |
| mysql | 0.8 - 10.6% | ~457 - 460 MiB, flat |
| apigateway | 0.1 - 2.0% | 60 - 82 MiB |
| medicalrecordservice | 0.1 - 1.4% | 67 - 91 MiB |
| patientservice / authservice | under 0.3% | flat |
| kafka | 1.5 - 7.9%, one isolated 82% snapshot at 16:44:33 | 420 - 476 MiB |

Full samples: `load-SWC-126-docker-stats.log`. Kafka also showed 65-166% in the two snapshots
before the run started (16:35-16:36), so its spikes are not driven by this workload, which
publishes about one completion event every 6 s. The 16:44:33 spike did not affect latency:
drift stayed at 1.00. Every other container stayed under 11% CPU with flat memory.

## Verdict - against TEST-PLAN.md section 9.5 (Load)

| Criterion | Threshold | Actual | Pass? |
|---|---|---:|:--:|
| Reads p95 | 800 ms or less | 14 ms | PASS |
| Reads p99 | 1500 ms or less | 22 ms | PASS |
| Writes p95 | 1500 ms or less | 42 ms | PASS |
| Writes p99 | 3000 ms or less | 54 ms | PASS |
| Error rate, all requests | 0.5% or less | 0.00% (0 of 5,161) | PASS |
| Latency drift | within 20% | 1.00 reads, 1.00 writes | PASS |

**Overall: PASS.** No Duration Assertion fired: the slowest sample of the run, 384 ms, is well
under the 1500 ms read and 3000 ms write tripwires.

## Post-run verification (local databases)

Read-only `SELECT` checks against `swiftcare_queue`, `swiftcare_medical_record` and
`swiftcare_prescription` after the run (queries in `README.md`):

| Check | Result |
|---|---|
| Every COMPLETE consultation has a Completed queue entry, and every Completed queue entry has a COMPLETE consultation | 148 = 148, 0 mismatches |
| No consultation has more than one prescription | 0 duplicates |
| Every prescription belongs to a COMPLETE consultation | 0 exceptions |
| Every DISPENSED prescription has both DispensedAt and DispensedBy; no PENDING one has either | 141 dispensed, 0 inconsistent |
| The medicine added in each cycle was removed again | 292 items = 2 per prescription, 0 leftover |
| Queue entry marked Completed before the doctor saved the prescription | 146 of 146, at least 3.6 s earlier (average 5.9 s) |

Totals include the Smoke gate run on the same database. At the end, 3 queue entries were still
InConsultation (2 with a consultation in progress, 1 called with no consultation yet) and 5
prescriptions were PENDING, all saved in the final 43 s. These are the cycles the 900 s
duration cut off part-way, matching call-next 145 against complete 142 in the JTL. Re-running
`seed-clinical-flow.ps1` completes them before the next run.

## Log scan

`docker compose logs` for the run window (16:36:00 to 16:51:30 IST), searched for
fail / error / exception / warn / crit:

| Service | Lines in window | Matches |
|---|---:|---:|
| prescriptionservice | 22,884 | 0 |
| queueservice | 16,924 | 0 (no Kafka consumer errors) |
| apigateway | 10,506 | 0 |
| medicalrecordservice | 0 | 0 |

MedicalRecordService writes only its startup lines (8 since the container started). At its
configured log levels it logs no requests and no successful completion publishes, so a clean
scan here shows the absence of warnings and errors, not activity. The completion flow is
instead confirmed by the database checks above. Recorded as an observability gap to raise with
the team, carried from the Sprint 2 QA recommendation.

## Analysis

At the modelled clinic peak (10 doctors running about one consultation a minute each, and 10
counter sessions refreshing every 5 s), the whole Sprint 3 workflow ran without a single error
and with latencies one to two orders of magnitude inside the thresholds. The slowest Sprint 3
step at p95 is consultation completion (46 ms), which includes the Kafka publish. Dispensing
(39 ms) and prescription creation (40 ms) follow. Every container stayed under 11% CPU, so the
system is effectively idle at this profile and the result says nothing about where it breaks.
That is what the Stress run is for.

The asynchronous parts kept pace. Every queue entry was marked Completed before the doctor saved
the prescription, 3.6 s or more after completion was requested. The average gap of 5.9 s
matches the think time between those two steps, so propagation through Kafka and QueueService
added no visible delay. Every doctor was free again at the first check before calling the next
patient (145 of 145 current-patient polls returned 204). The post-run checks found no duplicate
prescription, no mismatch between consultation and queue state, and no half-dispensed
prescription.

Two points to watch in Stress:

- `GET /api/prescriptions/pending` returns every PENDING prescription ever created, and
  `Prescriptions.Status` has no index. It stayed fast here because the counter kept the list
  short (response 4-8 KB), but its cost grows with any backlog. With think time cut and the
  counter falling behind the doctors, this is the likeliest endpoint to degrade first.
- The 40-50 s pause before each call-next hides any completion lag. Under Stress, the number of
  current-patient polls per cycle becomes a direct measure of how far QueueService falls behind
  the completion events.
