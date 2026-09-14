# PerformanceTest / azure

JMeter Smoke + Load runs against the real Azure deployment, a second, network-inclusive
data point alongside the local run in [`../local/`](../local/). Additive: nothing in
`../local/` depends on anything here.

Built for Sprint 1 (SWC-67) and extended with the Sprint 2 endpoints (SWC-88): the public
display, the Receptionist full queue, the Doctor waiting pool and consultation creation.

- Plan and pass/fail thresholds: [`TEST-PLAN-AZURE.md`](TEST-PLAN-AZURE.md)
- Filled results: [`results/`](results/)

## Files

| File | Purpose |
|---|---|
| `TEST-PLAN-AZURE.md` | The Azure plan. Section 6.2 (QueueService) is deferred; section 7 records the cold-start and single-replica limitations. |
| `swiftcare-load-azure.jmx` | Sprint 1 mix (from `../local/swiftcare-load.jmx`) plus the four Sprint 2 Thread Groups. Every group is sized by its own `-J` property and the Sprint 2 groups default to 0, so the SWC-67 commands are unchanged. Reads `data/*.csv` from this folder. |
| `user-azure.properties` | Copy of `../local/user.properties` plus `host=api.swiftcare.me`, `port=443`, `protocol=https`. |
| `seed-azure.ps1` | Two-token seed: the bootstrapped `admin` creates the Receptionist load-user pool, then `load.user.001` registers the patients (`POST /api/patients` rejects Admin). `-DoctorCount` then creates the Doctor pool and pre-calls one patient per doctor for the consultation sampler. Writes `data/*.csv`. |
| `data/*.csv.example` | Shape references. `users.csv`, `patients.csv`, `search-terms.csv`, `doctors.csv` and `called-queue.csv` are generated and git-ignored. |
| `results/RESULT-smoke-azure-20260907.md` | Smoke run report. |
| `results/RESULT-load-azure-20260907.md` | Load run report. |
| `results/RESULT-queueservice-verification-azure-20260907.md` | Records why section 6.2 was deferred and what unblocks it. |
| `results/RESULT-smoke-azure-20260914.md` | Sprint 2 Smoke gate report (SWC-88). |
| `results/RESULT-load-azure-20260914.md` | Sprint 2 Load run report (SWC-88), compared against the 2026-09-07 baseline. |

## Prerequisites

- Apache JMeter 5.6.3 on the `PATH`. Java 17+.
- The bootstrapped Azure admin credentials (set `AZURE_ADMIN_PASSWORD` before seeding).
- DevOps notified before running (shared infrastructure).

## Run

From `tests/PerformanceTest/azure/`. See `TEST-PLAN-AZURE.md` section 9 for the full command
set. In short:

```powershell
# set AZURE_ADMIN_PASSWORD first
./seed-azure.ps1 -UserCount 10 -PatientCount 200 -DoctorCount 70

# Sprint 2 endpoints (SWC-88): warm, Smoke gate, then Load
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersDisplay=1 -JusersToday=1 -JusersWaiting=1 -JusersConsult=0 `
  -Jrampup=1 -Jduration=60 -l results/warmup-swc88.jtl

jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersDisplay=1 -JusersToday=1 -JusersWaiting=1 -JusersConsult=1 `
  -Jrampup=1 -Jduration=60 -JwriteDelay=10000 -JwriteRange=2000 `
  -l results/smoke-swc88.jtl -e -o results/smoke-swc88-report

# the Smoke gate consumed the first rows of called-queue.csv; drop them first
$used = 4            # rows the Smoke gate consumed: one per usersConsult thread, per iteration
$rows = Get-Content data/called-queue.csv
@($rows[0]) + $rows[($used + 1)..($rows.Count - 1)] | Set-Content data/called-queue.csv

jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties `
  -Jthreads=0 -JusersDisplay=8 -JusersToday=4 -JusersWaiting=4 -JusersConsult=4 `
  -Jrampup=30 -Jduration=900 `
  -l results/load-swc88.jtl -e -o results/load-swc88-report
```

The Sprint 1 commands are unchanged; see `TEST-PLAN-AZURE.md` sections 9 and 11.6.

## Known limitations (stated, not defects)

- Every app runs `min-replicas=0, max-replicas=1, 0.25 vCPU / 0.5 GiB`. No autoscaling. Cold
  start after idle is about 20 to 25 s; the Load run needs about 3 minutes to reach steady
  state, so its measurement window starts at t+180 s (documented deviation in the Load report).
- No server-side APM. JMeter client-side timings only.
- Seeding and the Load run write real rows into `swiftcare_patient` and `swiftcare_queue`.
  There is no `docker compose down -v` equivalent.
- One pre-called queue entry is worth exactly one consultation, and costs one Doctor account
  with its own room. `data/called-queue.csv` is consumed, not recycled, so the SWC-24 group
  ends when it runs out. Seed more rows with `-DoctorCount`; do not raise the write rate.
- Nothing closes a consultation yet, so the pre-called queue entries stay `IN_CONSULTATION`
  in the deployed database after the run.
