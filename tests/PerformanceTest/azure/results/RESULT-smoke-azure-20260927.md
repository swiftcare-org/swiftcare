# Performance run report - SMOKE (Azure, Sprint 3 clinical flow)

**Jira:** SWC-127  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 12

Gate for the Load run in [`RESULT-load-azure-20260927.md`](RESULT-load-azure-20260927.md). One Doctor workflow thread and one Receptionist counter thread against the deployed Azure environment, to prove every Sprint 3 sampler is wired correctly before putting 20 users on shared infrastructure.

## Run metadata

| Field | Value |
|---|---|
| Run type | Smoke (gate) |
| Date / time | 2026-09-27 20:20 to 20:21 IST |
| JMeter version | 5.6.3, CLI (non-GUI), on Java 21.0.12 |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=0 -JusersClinicDoctor=1 -JusersClinicCounter=1 -Jrampup=1 -Jduration=60 -JcycleDelay=5000 -JcycleRange=0 -JdispenseAfterMs=5000 -l results/smoke-swc127.jtl -e -o results/smoke-swc127-report` |
| Target | `https://api.swiftcare.me`, through Cloudflare, to Azure Container Apps (eastasia) |
| Deployment | Current `develop` deployed by CD, 0.25 vCPU / 0.5 GiB, one replica per app |
| Warm-up before this run | `results/warmup-swc127.jtl`: 1 + 1 users, 60 s, 49 samples, 0 errors, discarded |
| Seeded data | `seed-clinical-flow-azure.ps1`: 10 Doctor and 10 Receptionist accounts, 200 patients checked in; doctors reset with `-QueueVolume 0` after the warm-up |

## Results

48 samples over 59 s, 0 errors, no assertion failures, all 17 sampler labels present. Excluding the two setup logins, p95 was 296 ms and the maximum 314 ms.

| Sampler | n | Codes | avg (ms) | max (ms) |
|---|---:|---|---:|---:|
| `GET /api/queue/today/current` | 3 | 204 x 3 | 142 | 151 |
| `PUT /api/queue/call-next` | 3 | 200 x 3 | 194 | 238 |
| `GET /api/consultations/patient/{id}/latest-follow-up` | 3 | 204 x 3 | 146 | 155 |
| `POST /api/consultations` | 3 | 201 x 3 | 160 | 168 |
| `POST /api/consultations/{id}/vitals` | 2 | 201 x 2 | 199 | 249 |
| `POST /api/consultations/{id}/complete` | 2 | 200 x 2 | 240 | 274 |
| `GET /api/consultations/latest-completed` | 2 | 200 x 2 | 135 | 146 |
| `GET /api/prescriptions/patient/{id}` | 2 | 200 x 2 | 133 | 136 |
| `POST /api/prescriptions` | 2 | 201 x 2 | 156 | 161 |
| `POST /api/prescriptions/{id}/items` | 2 | 201 x 2 | 157 | 158 |
| `DELETE /api/prescriptions/{id}/items/{medicineId}` | 2 | 200 x 2 | 159 | 160 |
| `GET /api/queue/today` | 8 | 200 x 8 | 286 | 314 |
| `GET /api/prescriptions/pending` | 8 | 200 x 8 | 142 | 159 |
| `GET /api/prescriptions/queue/{queueId}` | 2 | 200 x 2 | 139 | 146 |
| `PUT /api/prescriptions/{id}/dispense` | 2 | 200 x 2 | 203 | 247 |
| `POST /api/auth/login` (Doctor, setup) | 1 | 200 | 2,199 | 2,199 |
| `POST /api/auth/login` (Receptionist, setup) | 1 | 200 | 2,213 | 2,213 |

Login takes about 2.2 s: BCrypt verification on the 0.25 vCPU AuthService replica. It is a once-per-thread setup step, as in every earlier round.

## Verdict

**PASS.** Every sampler returned its expected status and every body assertion held. The Sprint 3 chain ran end to end on the deployed environment, including completion through the deployed Kafka, prescription creation and editing, and dispensing at the counter. No Cloudflare challenge or block appeared. Load run authorised.

## Earlier attempt, superseded

The first Smoke attempt (20:01 IST) ran without the warm-up. It returned 42 samples with every status 2xx and all 17 labels present, but the first `GET /api/prescriptions/pending` took 4,873 ms against the 2,000 ms tripwire, a cold start. The doctor reset that followed stopped with Cloudflare `error code: 1015` (rate limited) at the seventh login in a few seconds.

The Cloudflare rate-limiting rule was disabled by DevOps for the test window. The warm-up, Smoke and Load were then run in order, and this report records that clean sequence. The rate limit is recorded as an environment finding in the Load report.
