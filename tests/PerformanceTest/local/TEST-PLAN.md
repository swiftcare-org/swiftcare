# SwiftCare Sprint 1 - Performance Test Plan

**Jira:** SWC-67  **Tool:** Apache JMeter 5.6.x  **Target:** Sprint 1 API slice

## 1. Objective

Establish, on the local `docker-compose` environment:

1. **Baseline** - latency and throughput at expected clinic load.
2. **Headroom** - how far past expected load the system holds before latency or
   errors breach the thresholds in §5.
3. **Bottleneck** - which resource saturates first at the breaking point.

Results are for relative comparison (before/after a change) and for locating the
knee **on this environment**. They are not an absolute capacity guarantee for
production hardware.

## 2. Scope

- **In:** AuthService, PatientService, API Gateway, MySQL, Kafka - exercised only
  through the API Gateway on `:8000` (so JWT validation, header rewriting and
  YARP routing are part of what is measured).
- **Out:** the frontend (tested separately via Selenium E2E), QueueService and
  the other unbuilt services, CI integration, production-representative
  infrastructure.

## 3. Assumptions (locked before execution)

| # | Assumption |
|---|---|
| A1 | Small-clinic scale: normal ≈ 10 concurrent staff sessions, peak ≈ 20. |
| A2 | The first Load run *establishes* the baseline; later runs compare against it. |
| A3 | Login is a once-per-thread setup step, not part of the request mix (staff authenticate once per shift, not per action). |
| A4 | Create-user is excluded from the mix (admin-only onboarding, rare). |
| A5 | JWT lifetime ≈ 1 h; runs stay under that, so no mid-run re-auth is modelled. |

## 4. Workload model

Weights are by **controller execution**. Row 2 issues two HTTP requests per
execution, so by raw request count reads are > 85% of traffic.

| # | Action | Method + path | Weight | Data source |
|---|---|---|---:|---|
| - | Login | `POST /api/auth/login` | setup | `data/users.csv` |
| 1 | Search patient | `GET /api/patients/search?q={term}` | 55% | `data/search-terms.csv` |
| 2 | Open patient profile | `GET /api/patients/{id}` then `GET /api/patients/{id}/allergies` | 30% | `data/patients.csv` |
| 3 | Register patient | `POST /api/patients` | 10% | generated (unique NIC + phone per request) |
| 4 | Add allergy | `POST /api/patients/{id}/allergies` | 5% | `data/patients.csv` |

**Justification.** SWC-12 states patient search is "used dozens of times daily" -
every patient interaction begins with a lookup, and opening a profile is the
usual follow-up, so reads dominate. Registration happens only for new patients;
allergy edits are occasional. The mix is deliberately read-heavy to match a
clinic front desk, not balanced across CRUD.

**Think time:** Gaussian, 1000 ms ± 1500 ms between actions.

## 5. Pass / fail criteria (defined in advance)

**Measurement rules.** Discard the ramp-up period and the first 30 s of steady
state. Split endpoints into **reads** (search, profile, allergies) and **writes**
(register, add allergy). "Sustained" = holds for ≥ 60 s.

### 5.1 Load run - PASS requires all six

Profile: 20 users, 30 s ramp, 15 min steady state.

| Metric | Threshold |
|---|---|
| Reads p95 | ≤ 800 ms |
| Reads p99 | ≤ 1500 ms |
| Writes p95 | ≤ 1500 ms |
| Writes p99 | ≤ 3000 ms |
| Error rate (all requests) | ≤ 0.5% |
| Latency drift | last-third p95 within 20% of first-third p95 |

### 5.2 Stress run - breaking point

Profile (calibrated after the Load baseline): ramp 10 → **400** users over 10 min,
against the **CPU/memory-capped stack** (`docker-compose.perf.yml`), think-time
reduced to 300 ± 300 ms so request rate actually climbs with concurrency.

> Deviation from the pre-registered plan: the original ceiling was 80 users with
> 1000 ± 1500 ms think time. The Load run showed the uncapped system idle at
> 20 users (read p95 8 ms, 0 errors), so 80 users would not have reached a knee.
> The thresholds below are unchanged.

The **breaking point** is the first point at which any one of these holds for
≥ 60 s:

| Condition | Trigger |
|---|---|
| Aggregate p95 latency | > 2000 ms |
| Aggregate error rate | > 1% |
| Saturation | throughput (req/s) flat or falling while active threads still increasing |

**Record at that point:** active thread count, throughput (req/s), aggregate p95,
error rate, and the first resource to saturate (container CPU / memory from
`docker stats`, MySQL `Threads_connected` vs `max_connections`, etc.).

### 5.3 Rationale for the numbers

- 800 ms read p95 ≈ "a receptionist does not perceive lag" for an interactive
  lookup.
- 2000 ms / 1% are the conventional "degraded" lines for an internal
  line-of-business API.
- The drift check catches slow leaks / pool exhaustion that a point-in-time p95
  misses.

## 6. Test types

| Type | Profile | Status |
|---|---|---|
| Smoke | 1 user, 60 s | Required - gate for the rest |
| Load | 20 users / 30 s / 900 s | Required |
| Stress | 80 users / 480 s ramp / 600 s | Required |
| Spike | normal → 3× for 2 min → normal | Stretch |
| Soak | normal load, 1-2 h (watch memory, MySQL connections, GC, latency drift) | Stretch |

## 7. Environment

- Local `docker-compose`, full Sprint 1 stack.
- For attributable stress results, apply `docker-compose.perf.yml` (per-service
  CPU/memory limits) and record the limits used.
- Load generator (JMeter) and system-under-test on the same machine - acceptable
  for relative comparison; note it as a limitation.
- Observability: JMeter client-side timings + `docker stats` + service structured
  logs + MySQL `SHOW GLOBAL STATUS`. No APM in the services yet.

## 8. Deliverables

- `swiftcare-load.jmx`, `user.properties`, `seed.ps1`, `data/*.csv.example`,
  `docker-compose.perf.yml`, this plan, `README.md`.
- A filled `results/REPORT-*.md` for the Smoke, Load and Stress runs, each with
  the metrics table, a pass/fail verdict against §5, and a short analysis of the
  breaking point and first-saturating resource.

## 9. Sprint 3 clinical flow (SWC-126)

Sections 1 to 8 describe the Sprint 1 suite and still hold for it. This section adds the
Sprint 3 clinical workflow as a separate plan, `SWC-126-clinical-flow.jmx`, with its own seed
script and results. It keeps the same tool, measurement rules, thresholds and capped-stack
approach.

### 9.1 Objective

Establish a Load baseline and a Stress breaking point for vital signs, consultation completion,
prescriptions and the dispensing counter. Name the first resource to saturate. These endpoints
are chained writes across MedicalRecordService, PrescriptionService and QueueService, joined by
the consultation-completed Kafka event, so the plan drives them in the order a real
consultation runs.

### 9.2 Scope

**In**, all through the API Gateway on `:8000`:

| Story | Endpoint | Role | Class |
|---|---|---|---|
| SWC-28 | `GET /api/consultations/patient/{id}/latest-follow-up` | Doctor | read |
| SWC-25 | `POST /api/consultations/{id}/vitals` | Doctor | write |
| SWC-26 / SWC-38 | `POST /api/consultations/{id}/complete` | Doctor | write |
| SWC-29 | `GET /api/consultations/latest-completed` | Doctor | read |
| SWC-29 | `GET /api/prescriptions/patient/{id}` | Doctor | read |
| SWC-29 | `POST /api/prescriptions` | Doctor | write |
| SWC-40 | `POST /api/prescriptions/{id}/items` | Doctor | write |
| SWC-40 | `DELETE /api/prescriptions/{id}/items/{medicineId}` | Doctor | write |
| SWC-41 | `GET /api/prescriptions/pending` | Receptionist | read |
| SWC-30 | `GET /api/prescriptions/queue/{queueId}` | Receptionist | read |
| SWC-41 | `PUT /api/prescriptions/{id}/dispense` | Receptionist | write |

**Reported, not pass/fail:** the Sprint 2 endpoints the workflow cannot run without:
`GET /api/queue/today/current`, `PUT /api/queue/call-next`, `POST /api/consultations` and the
counter's `GET /api/queue/today`.

**Out:** the deployed environment (SWC-127), Spike and Soak, CI integration, the frontend.

### 9.3 Assumptions (locked before execution)

| # | Assumption |
|---|---|
| B1 | Clinic peak is 20 concurrent staff sessions, as in SWC-67: 10 doctors and 10 counter sessions. |
| B2 | A doctor completes about one consultation a minute under Load, roughly ten times a real clinic's rate. |
| B3 | The counter screen refreshes the queue and the pending list together every ~5 s (`QueueManagementPage.tsx`). |
| B4 | A patient reaches the counter no earlier than 30 s after the doctor saves the prescription, so the doctor has finished editing it. |
| B5 | Login is a once-per-thread setup step, not part of either class (A3). |

### 9.4 Workload model

**Doctor workflow** (`usersDoctor`). Each thread logs in as its own seeded doctor and repeats:

1. Poll `GET /api/queue/today/current` until it answers 204. Completion frees the doctor only
   once QueueService consumes the completion event. No delay before the first poll, then
   500 ms between polls, up to 20.
2. `PUT /api/queue/call-next`.
3. Latest follow-up, create consultation, record vital signs, complete.
4. Latest completed consultation, prescription history.
5. Create the prescription (2 medicines), add a third, remove it.
6. Pause before the next patient.

Think time is 1-3 s between steps and 40-50 s before the next patient. If call-next does not
return a patient, the rest of that cycle is skipped.

**Receptionist counter** (`usersCounter`). Each thread logs in, then every 4.5-5.5 s polls
`GET /api/queue/today` and `GET /api/prescriptions/pending`. It dispenses one pending
prescription when all of these hold:

- one of this suite's doctors wrote it, so manual QA data is never touched;
- its id hashes to this thread's number, so two threads never dispense the same one and a 409
  is a real defect;
- it is at least `dispenseAfterMs` old (B4).

Dispensing is `GET /api/prescriptions/queue/{queueId}` then
`PUT /api/prescriptions/{id}/dispense`.

### 9.5 Pass / fail criteria (defined in advance)

Measurement rules are those of section 5: discard the ramp-up and the first 30 s of steady
state; "sustained" means holding for 60 s or more. Reads and writes are the classes in 9.2.

**Load: PASS requires all six.**

| Metric | Threshold |
|---|---|
| Reads p95 | 800 ms or less |
| Reads p99 | 1500 ms or less |
| Writes p95 | 1500 ms or less |
| Writes p99 | 3000 ms or less |
| Error rate, all requests | 0.5% or less |
| Latency drift | last-third p95 within 20% of first-third p95, per class |

**Stress: the breaking point** is the first point at which any of these holds for 60 s or
more: aggregate p95 above 2000 ms, aggregate error rate above 1%, or throughput flat or falling
while active threads still rise. Record active threads, throughput, p95, error rate and the
first resource to saturate.

Every sampler carries a Duration Assertion at its class's p99 budget (`readSlaMs` 1500,
`writeSlaMs` 3000) as a per-sample tripwire. Pass/fail is still decided on percentiles from
the JTL. The error rate is stated on response code, with duration breaches reported apart.

### 9.6 Test types

| Type | Profile | Status |
|---|---|---|
| Smoke | 1 Doctor + 1 Receptionist, 60 s, 5 s cycle pause | Required, gate for the rest |
| Load | 10 Doctor + 10 Receptionist, 30 s ramp, 900 s | Required |
| Stress | 150 Doctor + 150 Receptionist, linear ramp over 600 s, capped stack | Required |
| Stress, isolated | as Stress with `-JcounterQueuePoll=false` | Only if Stress saturates QueueService first. Deferred until SWC-128 is fixed (see below) |

**Stress calibration.** The Load run (`results/RESULT-load-SWC-126-20260927.md`) left every
container under 11% CPU at 20 users. As in section 5.2 and SWC-87, the ceiling is scaled about
×15 to 150 + 150 = 300 threads, keeping the 1:1 ratio. Think time drops to 100-300 ms between
steps, the counter polls every 300-600 ms, and dispensing starts 10 s after a save. The pause
before the next patient stays at 5-10 s. It bounds how many patients the run consumes: about
4,000 at an average of 80 doctor threads, so Stress is seeded with 6,000. The ramp is the whole
run, so the knee is read off threads against time.

A 6,000-entry day makes `GET /api/queue/today` return the whole day's queue on every counter
poll. That is SWC-87's known QueueService bottleneck and could hide the Sprint 3 services. If
the combined run shows QueueService saturating first, the isolated run repeats it without the
counter's queue poll, to find the Sprint 3 services' own knee.

**Deviation (2026-09-27).** The combined Stress run (`results/RESULT-stress-SWC-126-20260927.md`)
did show QueueService saturating first. It also found that concurrent call-next requests
deadlock in MySQL and return 500, raised as SWC-128 for Sprint 4. Every doctor cycle starts
with call-next, so the isolated run would still load the Sprint 3 doctor endpoints only
lightly until that is fixed. The isolated run is therefore deferred. Once SWC-128 is merged,
the Stress profile is re-run to verify it (0 call-next 500s), followed by the isolated run.

### 9.7 Test data

`seed-clinical-flow.ps1` creates:

- `perf.clinic.doctor.NNN` accounts, each with its own room `PC-NNN`;
- `perf.clinic.reception.NNN` accounts;
- `-QueueVolume` patient check-ins, which QueueService turns into today's Waiting entries.

Each doctor thread reads one account row, once. There must be at least as many doctor rows as
doctor threads: call-next allows one open consultation per doctor and per room, so a thread
with no row left stops. Re-running the seed completes any consultation an interrupted run left
open.

Comparable Load and Stress runs start from a fresh database. `GET /api/prescriptions/pending`
returns every PENDING prescription ever created, so leftover data changes what it measures.

### 9.8 Post-run verification

After Load and Stress, read-only queries (`README.md`) confirm:

- every COMPLETE consultation has a Completed queue entry, and the reverse;
- no consultation has more than one prescription;
- every prescription belongs to a COMPLETE consultation;
- every DISPENSED prescription has DispensedAt and DispensedBy, and no PENDING one has either;
- the medicine added in each cycle was removed.

MedicalRecordService, PrescriptionService, QueueService and Gateway logs for the run window
are searched for errors, warnings and Kafka consumer failures (carried from the Sprint 2 QA
report).

### 9.9 Deliverables

- `SWC-126-clinical-flow.jmx`, `seed-clinical-flow.ps1`, `data/clinic-*.csv.example`,
  MedicalRecordService and PrescriptionService limits in `docker-compose.perf.yml`, this
  section and the README runbook.
- `results/RESULT-load-SWC-126-<date>.md` and `results/RESULT-stress-SWC-126-<date>.md`, each
  opening with the load profile and time window (users, role split, ramp, duration, samples,
  throughput) before the percentiles, followed by the verdict, the post-run checks, the log
  scan and a short analysis.
