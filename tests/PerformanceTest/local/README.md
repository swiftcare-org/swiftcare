# PerformanceTest / local

JMeter load and stress tests for the Sprint 1 API (AuthService + PatientService +
API Gateway), driven through the Gateway on `:8000`. Jira: **SWC-67**.

This folder also holds a second, independent suite for the Sprint 2 queue-polling
endpoints (SWC-20, SWC-21, SWC-23) - see [SWC-87 queue polling](#swc-87-queue-polling)
below. It shares this folder's conventions but has its own plan file, seed script
and result naming.

The Azure counterpart (a second, network-inclusive run against the deployed
environment) lives in [`../azure/`](../azure/) and does not depend on anything here.

The plan, the workload justification and the **pre-defined** pass/fail thresholds
for the Sprint 1 suite are in [`TEST-PLAN.md`](TEST-PLAN.md). Record run results in
the `results/` folder following the format of the existing `RESULT-*.md` reports.

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
  docker compose -f docker-compose.yml -f tests/PerformanceTest/local/docker-compose.perf.yml up -d
  ```

- `AUTH_SEED_PASSWORD` set to the value in the repo-root `.env` (only needed for
  seeding).

## 1. Seed data (once)

Load tests need a realistic data volume; searching against 3 rows proves nothing.

```powershell
cd C:\swiftcare\tests\PerformanceTest\local
# set AUTH_SEED_PASSWORD to the value in C:\swiftcare\.env, then:
./seed.ps1                      # ~25 users, ~500 patients; use -PatientCount / -UserCount to change
```

This writes `data/users.csv`, `data/patients.csv`, `data/search-terms.csv`
(git-ignored). `*.csv.example` files show the expected shape.

To reset between runs: `docker compose down -v` then bring the stack back up and
re-seed.

## 2. Run

All commands are run from `tests/PerformanceTest/local/` (the `.jmx` resolves
`data/...` relative to the working directory). Non-GUI only; never run a real load
from the JMeter GUI.

### Smoke - validates the script (gate for the others)

```powershell
jmeter -n -t swiftcare-load.jmx -q user.properties `
  -Jthreads=1 -Jrampup=1 -Jduration=60 `
  -l results/smoke.jtl -e -o results/smoke-report
```

Pass = every sampler 2xx, zero assertion failures, `token` extracted (no
`LOGIN_FAILED`).

### Load - the baseline

```powershell
jmeter -n -t swiftcare-load.jmx -q user.properties `
  -Jthreads=20 -Jrampup=30 -Jduration=900 `
  -l results/load.jtl -e -o results/load-report
```

### Stress - find the knee

Run against the CPU/memory-capped stack so the knee is attributable:

```powershell
cd C:\swiftcare
docker compose -f docker-compose.yml -f tests/PerformanceTest/local/docker-compose.perf.yml up -d
cd tests/PerformanceTest/local
jmeter -n -t swiftcare-load.jmx -q user.properties `
  -Jthreads=400 -Jrampup=600 -Jduration=600 -Jthinkdelay=300 -Jthinkrange=300 `
  -l results/stress.jtl -e -o results/stress-report
```

Read the breaking point off the dashboard's **Active Threads Over Time** vs
**Response Times Over Time** / **error %** charts, against the triggers in
`TEST-PLAN.md` section 5.2. Afterwards, restore the normal stack with
`docker compose up -d` (drops the limits).

### While a run is going, in another terminal

```powershell
docker stats                                   # per-container CPU / memory
# MySQL connection use (MYSQL_ROOT_PASSWORD from the repo-root .env):
docker exec swiftcare-mysql-1 mysql -uroot -p"$env:MYSQL_ROOT_PASSWORD" `
  -e "SHOW GLOBAL STATUS LIKE 'Threads_connected'; SHOW GLOBAL STATUS LIKE 'Max_used_connections';"
```

## 3. Read the results

Open `results/<run>-report/index.html`. The key views:

- **APDEX** and the **Statistics** table (p95 / p99 / error % per label) feed the
  Load pass/fail table.
- **Response Times Over Time**, **Active Threads Over Time**, **Transactions Per
  Second** feed the Stress breaking point.

Write the run up as `results/RESULT-<type>-<yyyymmdd>.md` following the format of
the existing reports, and commit it (the raw `.jtl` and the generated `-report/`
folders are git-ignored).

## Notes / known limitations

- Results are environment-bound (local Docker). They are for relative comparison
  and locating the knee, not an absolute capacity figure.
- `POST /api/patients` publishes a `patient-checked-in` Kafka event. On this local
  stack QueueService was not consuming at the time of the 2026-08-27 runs, so
  events accumulated in the topic; harmless for these run lengths, but note broker
  disk on a long soak.
- The `.NET` services have no APM. Server-side signal = `docker stats` + service
  logs + MySQL `SHOW GLOBAL STATUS`.
- Registering patients and adding allergies during a run mutates the DB. Re-seed
  or `docker compose down -v` between comparable runs.
- MySQL 8.4 default `max_connections` is 151; AuthService + PatientService pools
  can approach that under stress. If you see connection errors before CPU
  saturates, that is a legitimate finding; record it, do not pre-emptively raise
  the limit unless you are specifically testing past it.

## SWC-87 - queue polling

`SWC-87-queue-polling.jmx` covers the three fixed-interval polling endpoints added
in Sprint 2: the public waiting-room display (SWC-23, unauthenticated), the full
queue view (SWC-20, Receptionist) and the doctor's shared waiting pool (SWC-21,
Doctor). All three read the same `QueueEntries` table on the same ~5 s cadence, so
this plan runs them as three independent Thread Groups in one file, sized
independently via `-J` properties - set any group's user count to 0 to isolate the
other two for a per-endpoint run.

`SWC-23-display.jmx` (the original single-endpoint plan) stays in this folder as
the SWC-23-only baseline; `SWC-87-queue-polling.jmx` is the combined suite used for
the Smoke/Load/Stress profiles below.

### Seed data (once)

Needs a Doctor and a Receptionist account pool, plus enough today-dated queue
volume that the three endpoints return realistic result sets:

```powershell
cd C:\swiftcare\tests\PerformanceTest\local
# set AUTH_SEED_PASSWORD to the value in C:\swiftcare\.env, then:
./seed-queue-polling.ps1        # 10 Doctor + 10 Receptionist accounts, 150 check-ins
```

Writes `data/doctors.csv` and `data/receptionists.csv` (git-ignored, credentials).
Queue entries are produced indirectly - PatientService's `patient-checked-in`
Kafka event is consumed by QueueService, which creates the today-dated `Waiting`
row. Give the stack a few seconds to drain the topic after seeding before running
a profile that expects the full volume.

### Run

Properties: `usersDisplay` (default 15), `usersToday` (default 10), `usersWaiting`
(default 10), `rampUp`, `duration` (seconds), `pollDelay`/`pollRange` (ms, default
4500/1000 - the real ~5 s poll; cut for Stress so request rate climbs with
concurrency).

```powershell
# Smoke - gate for the others
jmeter -n -t SWC-87-queue-polling.jmx -q user.properties `
  -JusersDisplay=1 -JusersToday=1 -JusersWaiting=1 -JrampUp=1 -Jduration=60 `
  -l results/smoke-SWC-87.jtl

# Load - 15/10/10 users, real poll interval, 10 min steady state
jmeter -n -t SWC-87-queue-polling.jmx -q user.properties `
  -JusersDisplay=15 -JusersToday=10 -JusersWaiting=10 -JrampUp=15 -Jduration=600 `
  -l results/load-SWC-87.jtl -e -o results/load-SWC-87-report

# Stress - against the capped stack, calibrated ceiling (see RESULT-stress-SWC-87-*.md
# for how 150/100/100 was chosen), reduced think time
docker compose -f docker-compose.yml -f tests/PerformanceTest/local/docker-compose.perf.yml up -d
jmeter -n -t SWC-87-queue-polling.jmx -q user.properties `
  -JusersDisplay=150 -JusersToday=100 -JusersWaiting=100 -JrampUp=600 -Jduration=600 `
  -JpollDelay=300 -JpollRange=300 `
  -l results/stress-SWC-87.jtl -e -o results/stress-SWC-87-report
docker compose up -d   # restore the uncapped stack afterwards
```

`docker-compose.perf.yml` was extended this ticket to add a `queueservice` limit
(1.0 CPU / 512 MB) - the Sprint 1 version of that file excluded it as out of scope
then. Results follow the same `RESULT-<type>-SWC-87-<date>.md` naming as the rest
of this folder.

**Known limitation, recorded for follow-up:** these runs used 219 today-dated
queue entries - realistic for a quiet day, not a busy one. The Stress result
identifies a CPU-bound, unindexed table scan as the saturating factor in
QueueService, so a production-scale row count is expected to lower the
concurrency at which it saturates, not raise it. Re-test once the queue table
holds that volume.
