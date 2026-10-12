# PerformanceTest / local

JMeter load and stress tests for the Sprint 1 API (AuthService + PatientService +
API Gateway), driven through the Gateway on `:8000`. Jira: **SWC-67**.

This folder also holds a second, independent suite for the Sprint 2 queue-polling
endpoints (SWC-20, SWC-21, SWC-23) - see [SWC-87 queue polling](#swc-87-queue-polling)
below. It shares this folder's conventions but has its own plan file, seed script
and result naming. A third suite covers the Sprint 3 clinical workflow - see
[SWC-126 clinical flow](#swc-126---clinical-flow).

The Azure counterpart (a second, network-inclusive run against the deployed
environment) lives in [`../azure/`](../azure/) and does not depend on anything here.

The plan, the workload justification and the **pre-defined** pass/fail thresholds
for the Sprint 1 suite are in [`TEST-PLAN.md`](TEST-PLAN.md). Record run results in
the `results/` folder following the format of the existing `RESULT-*.md` reports.

## Prerequisites

- Apache JMeter 5.6.x on the `PATH` (`jmeter --version`), running on Java 17 to 21. JMeter
  5.6.3's Groovy cannot compile scripts on Java 22 or later, which breaks the SWC-126 plan. If
  your default Java is newer, point JMeter at a JDK 21 without changing the system default:
  `$env:JM_LAUNCH = "C:\Program Files\Eclipse Adoptium\jdk-21...\bin\java.exe"`. The
  `java.version` line in `jmeter.log` shows which one a run used.
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

## SWC-126 - clinical flow

`SWC-126-clinical-flow.jmx` covers the Sprint 3 clinical workflow. Doctor threads repeat:
call the next patient, consultation, vital signs, complete, prescription, add a medicine,
remove it. Receptionist threads poll the counter screen and dispense. The two groups are sized
by `usersDoctor` and `usersCounter`. The plan, the calibration and the pass/fail thresholds are
in [`TEST-PLAN.md` section 9](TEST-PLAN.md#9-sprint-3-clinical-flow-swc-126). Every property
is listed in `user.properties`.

The plan uses Groovy scripts, so JMeter must run on Java 17 to 21 (see Prerequisites). The
frontend must not be running: its screens poll the API every 5 s and would add traffic the
plan does not count.

### Reset and seed

Comparable Load and Stress runs start from a fresh database, because
`GET /api/prescriptions/pending` returns every PENDING prescription ever created. From the repo
root:

```powershell
docker compose down -v
docker compose up -d --wait mysql kafka
foreach ($s in "authservice","patientservice","queueservice","medicalrecordservice","prescriptionservice") { docker compose run --rm --no-deps $s --migrate }
docker compose up -d          # Stress: docker compose -f docker-compose.yml -f tests/PerformanceTest/local/docker-compose.perf.yml up -d
docker compose ps             # all healthy
```

Then seed from `tests/PerformanceTest/local/`. There must be at least as many doctor accounts
as doctor threads, and more waiting patients than the run consumes:

```powershell
$env:AUTH_SEED_PASSWORD = ((Get-Content ..\..\..\.env | Where-Object { $_ -match '^AUTH_SEED_PASSWORD=' }) -replace '^AUTH_SEED_PASSWORD=', '').Trim('"')
./seed-clinical-flow.ps1                                                    # Smoke + Load: 10 / 10 / 300
./seed-clinical-flow.ps1 -DoctorCount 150 -ReceptionistCount 150 -QueueVolume 6000   # Stress
```

Re-running the seed also completes any consultation a stopped run left open, so doctors can
call again.

### Run

From `tests/PerformanceTest/local/`. For Load and Stress, record server-side usage in a second
window for the length of the run (every 55 s for Load, every 30 s for Stress):

```powershell
while ($true) { Get-Date -Format s | Add-Content results/<run>-docker-stats.log; docker stats --no-stream --format "{{.Name}} {{.CPUPerc}} {{.MemUsage}}" | Add-Content results/<run>-docker-stats.log; Start-Sleep 55 }
```

```powershell
# Smoke - gate for the others: every sample 2xx and all 17 labels present, dispense included
jmeter -n -t SWC-126-clinical-flow.jmx -q user.properties `
  -JusersDoctor=1 -JusersCounter=1 -JrampUp=1 -Jduration=60 `
  -JcycleDelay=5000 -JcycleRange=0 -JdispenseAfterMs=5000 `
  -l results/smoke-SWC-126.jtl -e -o results/smoke-SWC-126-report

# Load - 10 Doctor + 10 Receptionist, 30 s ramp, 900 s
jmeter -n -t SWC-126-clinical-flow.jmx -q user.properties `
  -JusersDoctor=10 -JusersCounter=10 -JrampUp=30 -Jduration=900 `
  -l results/load-SWC-126.jtl -e -o results/load-SWC-126-report

# Stress - capped stack, 150 + 150 ramped over the whole 600 s, reduced think time
jmeter -n -t SWC-126-clinical-flow.jmx -q user.properties `
  -JusersDoctor=150 -JusersCounter=150 -JrampUp=600 -Jduration=600 `
  -JstepDelay=100 -JstepRange=200 -JcycleDelay=5000 -JcycleRange=5000 `
  -JpollDelay=300 -JpollRange=300 -JdispenseAfterMs=10000 `
  -l results/stress-SWC-126.jtl -e -o results/stress-SWC-126-report

# Stress, isolated - only if QueueService saturates first in the combined run. Reset and seed
# again first, then the same command with -JcounterQueuePoll=false and stress-SWC-126-isolated names.
```

JMeter refuses to write into an existing `-o` report folder; delete it before re-running a
profile. `-Jduration` includes the ramp-up, as in every earlier run in this folder. Restore the
uncapped stack with `docker compose up -d` after Stress.

### Post-run verification

Read-only checks against the local databases, from the repo root. Every count should be 0. The
last one can be 1 or 2 if the run ended between adding and removing a medicine:

```powershell
$rootPw = ((Get-Content .env | Where-Object { $_ -match '^MYSQL_ROOT_PASSWORD=' }) -replace '^MYSQL_ROOT_PASSWORD=', '').Trim('"')
docker exec -e MYSQL_PWD=$rootPw swiftcare-mysql-1 mysql -uroot -e "
SELECT COUNT(*) AS complete_consultation_without_completed_queue FROM swiftcare_medical_record.Consultations c LEFT JOIN swiftcare_queue.QueueEntries q ON q.Id = c.QueueId WHERE c.Status = 'COMPLETE' AND (q.Status IS NULL OR q.Status <> 'Completed');
SELECT COUNT(*) AS completed_queue_without_complete_consultation FROM swiftcare_queue.QueueEntries q LEFT JOIN swiftcare_medical_record.Consultations c ON c.QueueId = q.Id AND c.Status = 'COMPLETE' WHERE q.Status = 'Completed' AND c.Id IS NULL;
SELECT COUNT(*) AS consultations_with_more_than_one_prescription FROM (SELECT ConsultationId FROM swiftcare_prescription.Prescriptions GROUP BY ConsultationId HAVING COUNT(*) > 1) d;
SELECT COUNT(*) AS prescription_without_complete_consultation FROM swiftcare_prescription.Prescriptions p LEFT JOIN swiftcare_medical_record.Consultations c ON c.Id = p.ConsultationId WHERE c.Status IS NULL OR c.Status <> 'COMPLETE';
SELECT COUNT(*) AS dispense_fields_inconsistent FROM swiftcare_prescription.Prescriptions WHERE (Status = 'DISPENSED') <> (DispensedAt IS NOT NULL AND DispensedBy IS NOT NULL);
SELECT COUNT(*) AS added_medicine_not_removed FROM swiftcare_prescription.PrescriptionItems WHERE MedicineName = 'Perf Cetirizine';"
```

Then search the service logs for the run window, taking the start and end times from
`jmeter.log`:

```powershell
foreach ($s in "medicalrecordservice","prescriptionservice","queueservice","apigateway") {
  $lines = docker compose logs --no-color --since 2026-09-27T16:36:00+05:30 --until 2026-09-27T16:51:30+05:30 $s
  "{0}: {1} lines, {2} matches" -f $s, @($lines).Count, @($lines | Select-String -Pattern 'fail|error|exception|warn|crit').Count
}
```

Results follow the `RESULT-<type>-SWC-126-<date>.md` naming. Keep the run's
`<run>-docker-stats.log` next to its report by adding it to `.gitignore`'s exceptions.

**Known limitations:**

- MedicalRecordService writes only its startup lines at its configured log levels, so its log
  scan shows the absence of warnings and errors, not activity. The database checks above
  confirm the completion flow instead.
- A run ends part-way through some cycles. A few queue entries stay InConsultation and a few
  prescriptions stay PENDING until the next seed.
