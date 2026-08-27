# PerformanceTest

JMeter load and stress tests for the Sprint 1 API (AuthService + PatientService +
API Gateway), driven through the Gateway on `:8000`. Jira: **SWC-67**.

The plan, the workload justification and the **pre-defined** pass/fail thresholds
are in [`TEST-PLAN.md`](TEST-PLAN.md). Record run results in
[`results/REPORT-template.md`](results/REPORT-template.md) (copy per run).

## Prerequisites

- Apache JMeter 5.6.x on the `PATH` (`jmeter --version`). Java 17+.
- The Sprint 1 stack running locally:

  ```powershell
  cd C:\swiftcare
  docker compose up -d          # mysql, kafka, zookeeper, authservice, patientservice, apigateway
  docker compose ps             # all healthy
  ```

  For attributable stress results, start it with the resource-limit override
  instead:

  ```powershell
  docker compose -f docker-compose.yml -f tests/PerformanceTest/docker-compose.perf.yml up -d
  ```

- `AUTH_SEED_PASSWORD` set to the value in the repo-root `.env` (only needed for
  seeding).

## 1. Seed data (once)

Load tests need a realistic data volume; searching against 3 rows proves nothing.

```powershell
cd C:\swiftcare\tests\PerformanceTest
$env:AUTH_SEED_PASSWORD = "<value from C:\swiftcare\.env>"
./seed.ps1                      # ~25 users, ~500 patients; use -PatientCount / -UserCount to change
```

This writes `data/users.csv`, `data/patients.csv`, `data/search-terms.csv`
(git-ignored). `*.csv.example` files show the expected shape.

To reset between runs: `docker compose down -v` then bring the stack back up and
re-seed.

## 2. Run

All commands are run from `tests/PerformanceTest/` (the `.jmx` resolves `data/…`
relative to the working directory). Non-GUI only; never run a real load from the
JMeter GUI.

### Smoke — validates the script (gate for the others)

```powershell
jmeter -n -t swiftcare-load.jmx -q user.properties `
  -Jthreads=1 -Jrampup=1 -Jduration=60 `
  -l results/smoke.jtl -e -o results/smoke-report
```

Pass = every sampler 2xx, zero assertion failures, `token` extracted (no
`LOGIN_FAILED`).

### Load — the baseline

```powershell
jmeter -n -t swiftcare-load.jmx -q user.properties `
  -Jthreads=20 -Jrampup=30 -Jduration=900 `
  -l results/load.jtl -e -o results/load-report
```

### Stress — find the knee

Run against the CPU/memory-capped stack so the knee is attributable:

```powershell
cd C:\swiftcare
docker compose -f docker-compose.yml -f tests/PerformanceTest/docker-compose.perf.yml up -d
cd tests/PerformanceTest
jmeter -n -t swiftcare-load.jmx -q user.properties `
  -Jthreads=400 -Jrampup=600 -Jduration=600 -Jthinkdelay=300 -Jthinkrange=300 `
  -l results/stress.jtl -e -o results/stress-report
```

Read the breaking point off the dashboard's **Active Threads Over Time** vs
**Response Times Over Time** / **error %** charts, against the triggers in
`TEST-PLAN.md` §5.2. Afterwards, restore the normal stack with
`docker compose up -d` (drops the limits).

### While a run is going, in another terminal

```powershell
docker stats                                   # per-container CPU / memory
# MySQL connection use:
docker exec swiftcare-mysql-1 mysql -uroot -p"<MYSQL_ROOT_PASSWORD>" `
  -e "SHOW GLOBAL STATUS LIKE 'Threads_connected'; SHOW GLOBAL STATUS LIKE 'Max_used_connections';"
```

## 3. Read the results

Open `results/<run>-report/index.html`. The key views:

- **APDEX** and the **Statistics** table (p95 / p99 / error % per label) → the
  Load pass/fail table.
- **Response Times Over Time**, **Active Threads Over Time**, **Transactions Per
  Second** → the Stress breaking point.

Copy `results/REPORT-template.md`, fill it in, commit the filled copy (the raw
`.jtl` and the generated `-report/` folders are git-ignored).

## Notes / known limitations

- Results are environment-bound (local Docker). They are for relative comparison
  and locating the knee, not an absolute capacity figure.
- `POST /api/patients` publishes a `patient-checked-in` Kafka event. QueueService
  (the consumer) is not built yet, so events accumulate in the topic - harmless
  for these run lengths; note broker disk on a long soak.
- The `.NET` services have no APM. Server-side signal = `docker stats` + service
  logs + MySQL `SHOW GLOBAL STATUS`.
- Registering patients and adding allergies during a run mutates the DB. Re-seed
  or `docker compose down -v` between comparable runs.
- MySQL 8.4 default `max_connections` is 151; AuthService + PatientService pools
  can approach that under stress. If you see connection errors before CPU
  saturates, that is a legitimate finding - record it, do not pre-emptively
  raise the limit unless you are specifically testing past it.
