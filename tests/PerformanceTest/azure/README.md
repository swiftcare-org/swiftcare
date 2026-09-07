# PerformanceTest / azure

JMeter Smoke + Load run against the real Azure deployment (SWC-67), a second,
network-inclusive data point alongside the local run in
[`../local/`](../local/). Additive: nothing in `../local/` depends on anything here.

- Plan and pass/fail thresholds: [`TEST-PLAN-AZURE.md`](TEST-PLAN-AZURE.md)
- Filled results: [`results/`](results/)

## Files

| File | Purpose |
|---|---|
| `TEST-PLAN-AZURE.md` | The Azure plan. Section 6.2 (QueueService) is deferred; section 7 records the cold-start and single-replica limitations. |
| `swiftcare-load-azure.jmx` | Copy of `../local/swiftcare-load.jmx`; the only change is the test name suffix. Reads `data/*.csv` from this folder. |
| `user-azure.properties` | Copy of `../local/user.properties` plus `host=api.swiftcare.me`, `port=443`, `protocol=https`. |
| `seed-azure.ps1` | Two-token seed: the bootstrapped `admin` creates the Receptionist load-user pool, then `load.user.001` registers the patients (`POST /api/patients` rejects Admin). Writes `data/*.csv`. |
| `data/*.csv.example` | Shape references. `data/users.csv`, `data/patients.csv`, `data/search-terms.csv` are generated and git-ignored. |
| `results/RESULT-smoke-azure-20260907.md` | Smoke run report. |
| `results/RESULT-load-azure-20260907.md` | Load run report. |
| `results/RESULT-queueservice-verification-azure-20260907.md` | Records why section 6.2 was deferred and what unblocks it. |

## Prerequisites

- Apache JMeter 5.6.3 on the `PATH`. Java 17+.
- The bootstrapped Azure admin credentials (set `AZURE_ADMIN_PASSWORD` before seeding).
- DevOps notified before running (shared infrastructure).

## Run

From `tests/PerformanceTest/azure/`. See `TEST-PLAN-AZURE.md` section 9 for the full command
set. In short:

```powershell
# set AZURE_ADMIN_PASSWORD first
./seed-azure.ps1

jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=1 -Jrampup=1 -Jduration=45 -l results/warmup-azure.jtl -e -o results/warmup-azure-report
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=1 -Jrampup=1 -Jduration=60 -l results/smoke-azure.jtl -e -o results/smoke-azure-report
jmeter -n -t swiftcare-load-azure.jmx -q user-azure.properties -Jthreads=20 -Jrampup=30 -Jduration=900 -l results/load-azure.jtl -e -o results/load-azure-report
```

## Known limitations (stated, not defects)

- Every app runs `min-replicas=0, max-replicas=1, 0.25 vCPU / 0.5 GiB`. No autoscaling. Cold
  start after idle is about 20 to 25 s; the Load run needs about 3 minutes to reach steady
  state, so its measurement window starts at t+180 s (documented deviation in the Load report).
- No server-side APM. JMeter client-side timings only.
- Seeding and the Load run write real rows into `swiftcare_patient` and `swiftcare_queue`.
  There is no `docker compose down -v` equivalent.
