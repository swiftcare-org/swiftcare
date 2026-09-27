# SwiftCare - Azure Deployment Performance Test Plan

**Jira:** SWC-67, extended by SWC-88 and SWC-127  **Tool:** Apache JMeter 5.6.3
**Target:** Real Azure deployment (AuthService, PatientService, API Gateway; QueueService present but see section 6.2)

Sections 1 to 10 are the original SWC-67 round, unchanged. Section 11 adds the Sprint 2 endpoints (SWC-20, SWC-21, SWC-23, SWC-24) to the same suite. Section 12 adds the Sprint 3 clinical workflow (SWC-127).

This is the additive Azure counterpart to the local plan at
[`../local/TEST-PLAN.md`](../local/TEST-PLAN.md). Nothing in the local setup
(`../local/TEST-PLAN.md`, `../local/results/`, `../local/data/*.csv`,
`../local/swiftcare-load.jmx`, `../local/user.properties`, `../local/seed.ps1`)
depends on anything here. The Azure round reuses the identical workload model through
`swiftcare-load-azure.jmx`, a byte-for-byte copy of `../local/swiftcare-load.jmx` whose only
change is reading `data/*.csv` from this `azure/` folder.

## 1. Objective

Establish, against the real Azure deployment, a second, production-representative data point
alongside the existing local results (SWC-67):

1. **Baseline** - latency and throughput at expected clinic load, against the actual deployed
   environment.
2. **Confirmation** - that the deployed system behaves consistently with local findings, once
   real network latency is accounted for.
3. **End-to-end verification** - that the asynchronous queue-creation flow (QueueService) keeps
   pace under the same load. Deferred this round; see section 6.2.

This explicitly does not aim to find a breaking point. Stress testing is out of scope.

This is additive evidence alongside the existing local SWC-67 results, not a replacement.

## 2. Scope

**In:**

- AuthService, PatientService, API Gateway, as deployed on Azure Container Apps, exercised only
  through the public Gateway endpoint (`https://api.swiftcare.me`).

**Out:**

- Stress / breaking-point testing.
- Any test that meaningfully increases load beyond normal expected usage.
- QueueService verification (section 6.2). The deployed QueueService has no read API and its
  database and Kafka broker are private to the Azure VNet, so none of the section 6.2 checks
  can be run from the load-generator machine this round. Recorded in
  `results/RESULT-queueservice-verification-azure-20260907.md`.

## 3. Locked assumptions

| # | Assumption |
|---|---|
| A1 | Same workload model as SWC-67, for direct comparability. |
| A2 | Same concurrency as local: 20 users, matching modelled clinic peak. |
| A3 | This run includes real internet latency between the test machine and Azure. Results are a separate, network-inclusive data point, not directly apples-to-apples with local numbers. |
| A4 | No Stress run. Only Smoke and Load are in scope. |
| A5 | QueueService verification is deferred (section 6.2). The service is deployed and consuming, but is not observable from outside the Azure VNet this round. |
| A6 | DevOps notified before running, given shared Azure infrastructure. |

## 4. Workload model (unchanged from SWC-67)

Identical to `../local/TEST-PLAN.md` section 4. Weights are by controller execution; row 2
issues two HTTP requests per execution, so by raw request count reads are over 85% of traffic.

| # | Action | Method + path | Weight | Data source |
|---|---|---|---:|---|
| - | Login | `POST /api/auth/login` | setup (once per thread) | `data/users.csv` |
| 1 | Search patient | `GET /api/patients/search?q={term}` | 55% | `data/search-terms.csv` |
| 2 | Open patient profile | `GET /api/patients/{id}` then `GET /api/patients/{id}/allergies` | 30% | `data/patients.csv` |
| 3 | Register patient | `POST /api/patients` | 10% | generated (unique NIC + phone per request) |
| 4 | Add allergy | `POST /api/patients/{id}/allergies` | 5% | `data/patients.csv` |

Row 3 is the trigger for QueueService: every simulated registration publishes a
`patient-checked-in` event that the deployed QueueService consumes into `swiftcare_queue`. A
Load run therefore writes about 900 real patients, about 450 real allergies and about 900 real
queue entries into the deployed databases (see section 7, "Data").

**Think time:** Gaussian, 1000 ms plus/minus 1500 ms between actions (unchanged).

Load users are Receptionist role, the only role permitted to both register a patient and add an
allergy. The bootstrapped Admin account creates that user pool but cannot itself register
patients (`POST /api/patients` is Receptionist-only).

## 5. Test types

| Type | Profile | Status |
|---|---|---|
| Smoke | 1 user, 60 s | Required, gate for Load |
| Load | 20 users, 30 s ramp, 15 min steady | Required |
| Stress | not run | Explicitly excluded this round |

## 6. Pass / fail criteria (defined in advance)

**Measurement rules** (same as local): discard the ramp-up and the first 30 s of steady state.
Split endpoints into reads (search, profile, allergies) and writes (register, add allergy).
"Sustained" means holds for 60 s or more.

### 6.1 API layer (Smoke + Load)

| Metric | Threshold |
|---|---|
| Reads p95 | 1200 ms or less |
| Reads p99 | 2000 ms or less |
| Writes p95 | 2000 ms or less |
| Writes p99 | 3500 ms or less |
| Error rate (all requests) | 0.5% or less |
| Latency drift | last-third p95 within 20% of first-third p95 |

Looser than the local thresholds specifically because real network round-trip time is now part
of what is measured, not because correctness expectations changed. Warm-up requests (cold
start, section 7) are excluded from all figures.

### 6.2 QueueService layer - DEFERRED this round

The plan's original QueueService checks (queue entries created for every registration; no
duplicate `(PatientId, QueueDate)` pairs; sequential numbering held under concurrency; Kafka
consumer-group lag returns to near-zero) cannot be executed this round:

- QueueService (SWC-19 / SWC-71) is deployed as a private background consumer with no HTTP
  ingress. It exposes only `/health`; no endpoint returns queue rows.
- Azure MySQL Flexible Server has `publicNetworkAccess=Disabled`; `swiftcare_queue` is
  reachable only from inside the Azure VNet.
- Kafka runs as a private Azure Container Instances group; there is no Kafka UI on Azure and no
  Application Insights / OpenTelemetry in the services.

Running section 6.2 needs a DevOps-opened path (temporary MySQL firewall rule plus
`QUEUE_DB_PASSWORD`, or `az containerapp exec` into the VNet, or a `swiftcare-logs` Log
Analytics query). It is tracked as a follow-up, not dropped. Note also that the local SWC-67
baseline contains no QueueService data (the service was not built at the time), so this is
net-new work rather than a local/Azure comparison.

## 7. Environment

- **Load generator:** local machine, over the internet, targeting `https://api.swiftcare.me`
  (Azure Container Apps, region `eastasia`;
  `swiftcare-gateway.jollymoss-1030e513.eastasia.azurecontainerapps.io`).
- **System under test:** the actual deployed Azure Container Apps: Gateway (external ingress),
  AuthService and PatientService (internal ingress), QueueService (no ingress).
- **Container sizing (stated limitation):** every app runs at `min-replicas=0, max-replicas=1,
  0.25 vCPU / 0.5 GiB`. There is no horizontal autoscaling; 20 concurrent users hit a single
  small replica of each service. Higher latency than the local Docker stack is expected; a
  section 6.1 breach may be the 0.25-vCPU cap or internet RTT rather than application code, and
  the reports must not attribute it to one cause without evidence.
- **Cold start (stated limitation):** apps scale to zero when idle; the first request after
  idle takes about 20 to 25 s (measured). Warm the deployment before the real Smoke run and
  exclude warm-up samples from all figures.
- **Data:** freshly seeded against the Azure Gateway URL by `seed-azure.ps1` (about 25 users,
  about 500 patients). Seeding and the Load run write real rows into `swiftcare_patient` and
  `swiftcare_queue`; there is no `docker compose down -v` equivalent. Agree with DevOps whether
  that test data stays or is cleaned afterwards.
- **Observability:** JMeter client-side timings only. No `docker stats` equivalent for Azure's
  managed infrastructure; no server-side APM. This is a stated limitation.

## 8. Deliverables

- `TEST-PLAN-AZURE.md` (this document)
- `swiftcare-load-azure.jmx`, `user-azure.properties`, `seed-azure.ps1`, `data/*.csv.example`
- `results/RESULT-smoke-azure-20260907.md`
- `results/RESULT-load-azure-20260907.md`
- `results/RESULT-queueservice-verification-azure-20260907.md` (records the section 6.2 deferral)

All reports follow the same format as the existing local results, for consistency, and each
states explicitly that this is a second, network-inclusive data point alongside the existing
local SWC-67 results, not a replacement.

## 9. Execution steps

All commands run from `tests/PerformanceTest/azure/`. Non-GUI only.

1. **Confirm timing with DevOps** (A6), shared infrastructure.

2. **Seed data against Azure.** Set `AZURE_ADMIN_PASSWORD` to the bootstrapped admin password
   first, then:

   ```powershell
   cd C:\swiftcare\tests\PerformanceTest\azure
   ./seed-azure.ps1                 # ~25 users, ~500 patients; -UserCount / -PatientCount to change
   ```

   Writes `data/users.csv`, `data/patients.csv`, `data/search-terms.csv` (git-ignored).

3. **Warm up**, then run **Smoke** (gate: every sampler 2xx, `token` extracted, no
   `LOGIN_FAILED`):

   ```powershell
   # warm-up: ignore these results, just wake the apps
   jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
     -Jthreads=1 -Jrampup=1 -Jduration=45 `
     -l results/warmup-azure.jtl -e -o results/warmup-azure-report

   # real Smoke
   jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
     -Jthreads=1 -Jrampup=1 -Jduration=60 `
     -l results/smoke-azure.jtl -e -o results/smoke-azure-report
   ```

4. **Run Load** (only if Smoke passes), record against section 6.1:

   ```powershell
   jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
     -Jthreads=20 -Jrampup=30 -Jduration=900 `
     -l results/load-azure.jtl -e -o results/load-azure-report
   ```

5. **QueueService verification (section 6.2):** deferred; see
   `results/RESULT-queueservice-verification-azure-20260907.md` for the rationale and the
   follow-up needed.

6. **Write up** `results/RESULT-smoke-azure-20260907.md` and
   `results/RESULT-load-azure-20260907.md` from each run's `*-report/index.html` (APDEX and
   Statistics table for section 6.1; Response Times / Active Threads / TPS over time for
   drift), explicitly noting this is a second, network-inclusive data point alongside local
   SWC-67.

## 10. Out of scope

- Stress / breaking-point testing against Azure this round.
- Direct load-testing of QueueService; it has no ingress by design.
- QueueService data/lag verification (section 6.2); deferred pending in-VNet access.
- Any change to the existing local SWC-67 results or artifacts; they remain as-is.

## 11. Sprint 2 extension (SWC-88)

Sections 1 to 10 describe the original SWC-67 round and still hold for it. This section adds the Sprint 2 endpoints to the same deployed-environment suite, so the Azure baseline reflects the current API surface instead of only the Sprint 1 one. Same target, same tool, same thresholds, same no-Stress decision.

### 11.1 Scope

**In:** four endpoints, all through the Gateway only.

| Story | Endpoint | Auth | Class |
|---|---|---|---|
| SWC-23 | `GET /api/queue/display` | none, the one public route | read |
| SWC-20 | `GET /api/queue/today` | Receptionist | read |
| SWC-21 | `GET /api/queue/today/waiting` | Doctor | read |
| SWC-24 | `POST /api/consultations` | Doctor, needs a pre-called queue entry | write |

**Out:** Stress against Azure (unchanged from section 5); QueueService-level verification, database row counts and Kafka consumer lag (unchanged from section 6.2, and for the same reason: `swiftcare_queue` and the broker are private to the Azure VNet); `PUT /api/queue/call-next` as a load sampler, since one doctor can hold only one open consultation at a time so it cannot be driven concurrently (it is used during seeding instead).

### 11.2 Workload model

Four independently sized Thread Groups in `swiftcare-load-azure.jmx`, one per endpoint. Every new group defaults to 0 users, so all the SWC-67 command lines in section 9 keep their original meaning; the Sprint 2 profile switches the Sprint 1 group off with `-Jthreads=0` and sizes the four new ones.

| Group | `-J` property | Load run | Pacing | Data source |
|---|---|---:|---|---|
| SWC-23 public display screens | `usersDisplay` | 8 | 4500 +/- 1000 ms, the UI poll cadence | none |
| SWC-20 Receptionist full queue | `usersToday` | 4 | 4500 +/- 1000 ms | `data/users.csv` |
| SWC-21 Doctor waiting pool | `usersWaiting` | 4 | 4500 +/- 1000 ms | `data/doctors.csv` |
| SWC-24 Doctor consultation writes | `usersConsult` | 4 | 60000 +/- 15000 ms | `data/doctors.csv` + `data/called-queue.csv` |

Total 20 concurrent users, 30 s ramp, 900 s steady, matching section 5 and the SWC-67 baseline concurrency so the two runs are comparable. The display group is the largest because in a real clinic waiting-room screens outnumber staff sessions and poll unconditionally.

The read mix is deliberately weighted 16 read users to 4 write users: on this deployment writes are rate-limited by the data model, not by the test (see 11.3), and the ticket's own acceptance criteria are split read/write.

### 11.3 Test data and the write-rate ceiling

`POST /api/consultations` answers a second consultation for the same `queueId` with 409, and `PUT /api/queue/call-next` allows one open consultation per doctor and per room. Nothing closes a consultation yet (no consumer moves the queue entry out of `IN_CONSULTATION`), so:

- one pre-called queue entry is worth exactly one 201, and
- one pre-called queue entry costs one Doctor account with its own room number.

`seed-azure.ps1 -DoctorCount N` therefore creates N doctors, pre-calls one patient for each, and writes N rows to `data/called-queue.csv`. The SWC-24 group consumes that file one row per request, never recycles it, and stops when it is exhausted (`recycle=false`, `stopThread=true`), so a drained pool ends the write group cleanly instead of turning the rest of the run into a wall of 409s. To keep the write group busy longer, seed more rows; do not raise the write rate.

At 4 users on a 60 s pacing the group asks for about 60 writes across a 900 s run, which is already about ten times a real clinic's consultation rate, and needs `-DoctorCount 70` to cover it.

Seeding for this round: `-UserCount 10 -PatientCount 200 -DoctorCount 70`, which leaves about 130 entries waiting so the SWC-21 pool is never empty during the run.

### 11.4 Assertions

Per sampler: a Response Assertion on the status code (200 for the three reads, 201 for the write), a Response Assertion on one or two contract fields of the body, and a Duration Assertion set to that class's p99 budget (`-JreadSlaMs`, default 2000 ms; `-JwriteSlaMs`, default 3500 ms).

The Duration Assertion is a per-sample tripwire, not the pass/fail rule. Section 6.1 is still decided on percentiles computed from the JTL. Because JMeter counts an assertion failure as a failed sample, the report states the error rate on response code (non-2xx / non-201), and reports duration-assertion breaches separately.

### 11.5 Pass / fail criteria

Section 6.1 thresholds are unchanged and apply per class: reads p95 1200 ms or less, reads p99 2000 ms or less, writes p95 2000 ms or less, writes p99 3500 ms or less, error rate 0.5% or less, and last-third p95 within 20% of first-third p95, sustained over 60 s or more. Reads are the three GET samplers; writes are `POST /api/consultations`. Per-thread logins are setup, not part of either class.

The cold-start deviation recorded in `results/RESULT-load-azure-20260907.md` applies here too: with `min-replicas=0, max-replicas=1, 0.25 vCPU`, the deployment needs a warm-up before the timed run, and warm-up samples are excluded from all figures.

### 11.6 Execution

From `tests/PerformanceTest/azure/`, non-GUI only, after confirming the window with whoever manages the deployment.

```powershell
# 1. Seed. Set AZURE_ADMIN_PASSWORD first.
./seed-azure.ps1 -UserCount 10 -PatientCount 200 -DoctorCount 70

# 2. Warm the scale-to-zero apps. Results discarded.
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersDisplay=1 -JusersToday=1 -JusersWaiting=1 -JusersConsult=0 `
  -Jrampup=1 -Jduration=60 -l results/warmup-swc88.jtl

# 3. Smoke, the gate for Load: 1 user per group, every sampler green.
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersDisplay=1 -JusersToday=1 -JusersWaiting=1 -JusersConsult=1 `
  -Jrampup=1 -Jduration=60 -JwriteDelay=10000 -JwriteRange=2000 `
  -l results/smoke-swc88.jtl -e -o results/smoke-swc88-report

# 4. The Smoke gate consumed rows from the top of called-queue.csv. Drop them, or the
#    Load run reopens the file at row 1 and its first writes answer 409.
$used = 4            # rows the Smoke gate consumed: one per usersConsult thread, per iteration
$rows = Get-Content data/called-queue.csv
@($rows[0]) + $rows[($used + 1)..($rows.Count - 1)] | Set-Content data/called-queue.csv

# 5. Load, only if Smoke passed.
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersDisplay=8 -JusersToday=4 -JusersWaiting=4 -JusersConsult=4 `
  -Jrampup=30 -Jduration=900 `
  -l results/load-swc88.jtl -e -o results/load-swc88-report
```

Write up `results/RESULT-load-azure-<date>.md` against 11.5, and compare it explicitly with `results/RESULT-load-azure-20260907.md` rather than presenting it as a standalone number.

### 11.7 Data footprint

A seeded run of this size adds about 200 patient rows, about 200 queue rows, 70 doctor accounts, 10 receptionist accounts and up to about 60 consultation rows to the deployed databases, and leaves about 70 queue entries in `IN_CONSULTATION` with no way to close them from outside the VNet. There is no `docker compose down -v` equivalent, so agree with DevOps beforehand whether that data stays. All of it is synthetic.

## 12. Sprint 3 extension (SWC-127)

Sections 1 to 11 still hold for their rounds. This section adds the Sprint 3 clinical workflow to the same deployed-environment suite. It keeps the same target, tool and section 6.1 thresholds, and the same no-Stress decision. The workload model is the one the local suite already ran (`../local/TEST-PLAN.md` section 9, SWC-126), so the two runs compare endpoint by endpoint.

### 12.1 Scope

**In**, all through the Gateway at `https://api.swiftcare.me`:

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

**Reported, not pass/fail:** the Sprint 2 endpoints the workflow cannot run without: `GET /api/queue/today/current`, `PUT /api/queue/call-next`, `POST /api/consultations` and the counter's `GET /api/queue/today`.

**Out:** Stress against Azure (unchanged from section 5). Database row checks and Kafka consumer lag (unchanged from section 6.2: the databases and the broker are private to the VNet). The local post-run checks in `../local/TEST-PLAN.md` section 9.8 cover consistency instead. The Sprint 1 and Sprint 2 groups are not re-run this round.

### 12.2 Workload model

Two Thread Groups in `swiftcare-load-azure.jmx`, copied from `../local/SWC-126-clinical-flow.jmx` with the same samplers, assertions and labels. Both default to 0 users, so every earlier command line keeps its meaning.

| Group | `-J` property | Load run | Pacing |
|---|---|---:|---|
| SWC-127 Doctor Clinical Workflow | `usersClinicDoctor` | 10 | 1-3 s between steps, 40-50 s before the next patient (about one consultation a minute) |
| SWC-127 Receptionist Counter | `usersClinicCounter` | 10 | queue and pending list every 4.5-5.5 s, dispense 30 s or more after a save |

That is 20 concurrent users, 30 s ramp and 900 s, matching sections 5 and 11.2. Each doctor thread repeats:

1. Wait until the doctor is free.
2. Call next.
3. Latest follow-up, consultation, vital signs, complete.
4. Latest completed consultation, prescription history.
5. Create the prescription, add a medicine, remove it.

Each counter thread dispenses only prescriptions written by this suite's doctors whose id hashes to its own thread number, so two threads never dispense the same one and a 409 is a real defect.

### 12.3 Test data

`seed-clinical-flow-azure.ps1` creates:

- the Doctor pool, `perf.clinic.doctor.NNN`, each with its own room `PC-NNN`;
- the Receptionist pool, `perf.clinic.reception.NNN`;
- `-QueueVolume` patient check-ins (default 200), registered by the first receptionist because Admin cannot register patients.

These accounts are separate from the Sprint 2 `load.doctor.*` pool.

Two constraints follow from the workflow:

- **Seed on the day of the run.** Check-ins become today's Waiting entries, and call-next only takes today's.
- **Reset the doctors between runs.** A run ends part-way through some cycles, leaving those doctors holding a called patient. `./seed-clinical-flow-azure.ps1 -QueueVolume 0` completes those consultations through the real APIs. Run it after the warm-up and after the Smoke gate.

### 12.4 Assertions

As in section 11.4: a status assertion on every sampler (200/201, and 204 accepted where the endpoint answers "none"), a body assertion on key contract fields, and a Duration Assertion at the class p99 budget (`readSlaMs` 2000 ms, `writeSlaMs` 3500 ms) as a tripwire. Pass/fail is decided on percentiles from the JTL. The error rate is stated on response code, with duration breaches reported apart.

### 12.5 Pass / fail criteria

Section 6.1 thresholds apply per class: reads p95 1200 ms or less, reads p99 2000 ms or less, writes p95 2000 ms or less, writes p99 3500 ms or less, error rate 0.5% or less, and last-third p95 within 20% of first-third p95. Reads and writes are the classes in 12.1. Warm-up samples are excluded. The measurement window discards the ramp-up and the first 30 s of steady state. If the deployment is still settling, the window moves as documented in `results/RESULT-load-azure-20260907.md`, and the report says so.

### 12.6 Environment since SWC-88

- **Cloudflare.** `api.swiftcare.me` is now proxied by Cloudflare, added outside this repository. API responses are not cached (`cf-cache-status: DYNAMIC`). A request sent with JMeter's user agent was answered 200 with JSON and no challenge. The Cloudflare edge adds a hop the Sprint 2 baseline may not have had, and the report states this when comparing.
- **Replicas.** CD now sets `min-replicas=1, max-replicas=1` at 0.25 vCPU / 0.5 GiB for every app. Apps no longer scale to zero while running, but the environment is stopped between sprints, so the warm-up still runs after it is started.
- **Deployment state.** The run needs the current `develop` deployed through CD, with the PrescriptionService and MedicalRecordService migrations applied, and the Kafka messaging layer recreated by Terraform.
- **Plan structure.** The Sprint 1 `users.csv`, `patients.csv` and `search-terms.csv` Data Sets moved from plan level into the Sprint 1 group (and `users.csv` into SWC-20), the only groups that read them. With `shareMode.all` the rows those groups read are unchanged. The SWC-127 groups no longer need the Sprint 1 seed files or share their variables.
- **JMeter on Java 17 to 21.** The SWC-127 groups use Groovy, which JMeter 5.6.3 cannot compile on Java 22 or later (see `README.md`).

### 12.7 Execution

From `tests/PerformanceTest/azure/`, non-GUI only, within the window agreed with DevOps.

```powershell
# 1. Seed, on the day of the run. Set AZURE_ADMIN_PASSWORD first.
./seed-clinical-flow-azure.ps1

# 2. Warm the deployment. Results discarded.
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersClinicDoctor=1 -JusersClinicCounter=1 -Jrampup=1 -Jduration=60 `
  -JcycleDelay=5000 -JcycleRange=0 -JdispenseAfterMs=5000 -l results/warmup-swc127.jtl

# 3. Reset any doctor the warm-up left mid-consultation, then run the Smoke gate:
#    every sample 2xx and all 17 labels present.
./seed-clinical-flow-azure.ps1 -QueueVolume 0
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersClinicDoctor=1 -JusersClinicCounter=1 -Jrampup=1 -Jduration=60 `
  -JcycleDelay=5000 -JcycleRange=0 -JdispenseAfterMs=5000 `
  -l results/smoke-swc127.jtl -e -o results/smoke-swc127-report

# 4. Reset again, then Load, only if Smoke passed.
./seed-clinical-flow-azure.ps1 -QueueVolume 0
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersClinicDoctor=10 -JusersClinicCounter=10 -Jrampup=30 -Jduration=900 `
  -l results/load-swc127.jtl -e -o results/load-swc127-report
```

Write up `results/RESULT-smoke-azure-<date>.md` and `results/RESULT-load-azure-<date>.md` against 12.5. Open each with the load profile and time window (users, role split, ramp, duration, warm-up excluded, samples, throughput). Compare them explicitly with the Sprint 2 Azure baseline (`results/RESULT-load-azure-20260914.md`) and with the local Sprint 3 run (`../local/results/RESULT-load-SWC-126-20260927.md`).

### 12.8 Data footprint

A seeded run adds these to the deployed databases:

- 10 doctor and 10 receptionist accounts;
- about 200 patient and queue rows;
- about 150 consultations with vital signs, about 150 completed queue entries, and about 150 prescriptions (most of them dispensed).

A few consultations and prescriptions stay open where the run's end cut a cycle off, until the next `-QueueVolume 0` reset. There is no reset equivalent on Azure, so agree with DevOps beforehand whether the data stays. All of it is synthetic.

After the run, shut the environment down as described in `deployment/terraform/README.md`, "Cost controls", unless DevOps says otherwise.
