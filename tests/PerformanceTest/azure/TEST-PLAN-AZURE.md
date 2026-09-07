# SwiftCare Sprint 1 - Azure Deployment Performance Test Plan

**Jira:** SWC-67  **Tool:** Apache JMeter 5.6.3
**Target:** Real Azure deployment (AuthService, PatientService, API Gateway; QueueService present but see section 6.2)

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
