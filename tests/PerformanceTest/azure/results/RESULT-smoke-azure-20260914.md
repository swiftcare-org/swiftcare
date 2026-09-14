# Performance run report - SMOKE (Azure, Sprint 2 endpoints)

**Jira:** SWC-88  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 11

Gate for the Load run in [`RESULT-load-azure-20260914.md`](RESULT-load-azure-20260914.md). One virtual user per Sprint 2 sampler against the deployed Azure environment, to prove every sampler is wired correctly before putting 20 users on shared infrastructure.

## Run metadata

| Field | Value |
|---|---|
| Run type | Smoke (gate) |
| Date / time | 2026-09-14 20:50 to 20:52 IST |
| JMeter version | 5.6.3 |
| Command used | `jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=0 -JusersDisplay=1 -JusersToday=1 -JusersWaiting=1 -JusersConsult=1 -Jrampup=1 -Jduration=60 -JwriteDelay=10000 -JwriteRange=2000 -l results/smoke-swc88.jtl -e -o results/smoke-swc88-report` |
| Target | `https://api.swiftcare.me` (Azure Container Apps, eastasia) |
| Warm-up before this run | `results/warmup-swc88.jtl`, 32 samples, 0 errors, max 2190 ms |
| Seeded data | `seed-azure.ps1 -UserCount 10 -PatientCount 200 -DoctorCount 70` |
| Branch / commit | `develop` at 4fa421c plus the SWC-88 working tree |

## Results

36 samples over 54 s, 0 errors, no assertion failures.

| Sampler | n | Codes | p50 (ms) | max (ms) |
|---|---:|---|---:|---:|
| `GET /api/queue/display` | 11 | 200 x 11 | 116 | 825 |
| `GET /api/queue/today` | 9 | 200 x 9 | 332 | 374 |
| `GET /api/queue/today/waiting` | 9 | 200 x 9 | 232 | 260 |
| `POST /api/consultations` | 4 | 201 x 4 | 136 | 1855 |
| `POST /api/auth/login` (setup) | 3 | 200 x 3 | 1667 | 2529 |

The first call of each thread carries TLS handshake and connect cost, which is the whole of the 825 ms display maximum and the 1855 ms consultation maximum; the later samples of both sit at their p50.

## Verdict

**PASS.** Every sampler returned its expected status (200 for the three reads, 201 for the write), every body assertion held, and the pre-called queue entries from `data/called-queue.csv` produced real consultations. Load run authorised.

## Carried forward to the Load run

This Smoke consumed the first four rows of `data/called-queue.csv`. Rows are consumed and never recycled, and a second consultation on the same `queueId` answers 409, so the Load run that follows starts at row 1 and its first four writes are expected 409s. That is recorded and quantified in the Load report rather than silently absorbed.
