# Performance run report - LOCAL VALIDATION of the Sprint 4 Azure plan (SWC-137)

**Jira:** SWC-137  **Plan:** [`TEST-PLAN-AZURE.md`](../TEST-PLAN-AZURE.md) section 13.6

Before the Azure round, the extended `swiftcare-load-azure.jmx` and `seed-sprint4-azure.ps1` were run against the isolated local SWC-136 stack. This proves the four new Thread Groups and the Azure seed work end to end, and gives a local result for the same 20-user profile to compare with Azure. It is not a pass/fail run: the section 13.5 criteria decide the Azure Load only.

## Outcome

- **Smoke: PASS.** 120 samples, 0 failures, all 31 sampler labels present.
- **20-user Load: clean.** 5,131 samples over 900 s, 0 errors, reads p95 26 ms, writes p95 57 ms.
- **One seed defect found and fixed** before the counted run (below). This is what the validation step is for: the Azure reset reuses existing accounts, which is the path that failed.

## Environment

| Item | Value |
|---|---|
| Date | 2026-10-12 |
| Target | `http://127.0.0.1:8016`, compose project `swiftcare-swc136`, fresh volumes and migrations, no resource caps (as the SWC-136 Load runs) |
| Seed | `seed-sprint4-azure.ps1 -GatewayUrl http://127.0.0.1:8016 -AdminUsername admin.fernando`: 10 Doctor, 10 Receptionist, 4 Admin/target pairs, 150 patients; topped up with another 150 before the counted run (237 waiting at its start) |
| JMeter | 5.6.3 on Temurin 21.0.12.1, load generator on the same host |
| Branch | `test/SWC-137-extend-deployed-environment-jmeter-suite` |

## Smoke

1 user in each of the six groups, 6 s ramp, 60 s, with the section 12.7 Smoke pacing (`cycleDelay=5000`, `cycleRange=0`, `dispenseAfterMs=5000`). 17:44:30-17:45:30 IST.

120 samples, 0 failures, 31 distinct labels: the 17 Sprint 3 labels, one shared `[SETUP] POST /api/auth/login` and the 13 Sprint 4 samplers.

## First Load attempt, not counted

17:46:20 IST, stopped at 725 s. 4,159 samples, 49 failures, every one `PUT /api/users/{id}` returning 404 from the Admin Operations thread. All other endpoints returned their expected status.

**Cause, a seed defect.** The doctor reset between Smoke and Load re-ran the seed, which found the Admin target accounts already present and looked them up with `@(Invoke-RestMethod ... /api/users)`. Windows PowerShell keeps the returned JSON array as one item inside `@(...)`, so the username filter matched the whole list and `targetId` became every user ID joined by spaces. Fixed in `seed-sprint4-azure.ps1` and in the same lookup in `../local/seed-sprint4.ps1` (latent there, because SWC-136 always seeded an empty database). The Azure seed now also rejects any `targetId` that is not a single GUID. After the fix the CSV held one GUID per target.

## Counted Load run

5 Doctor, 4 Counter, 4 Activity Feed, 4 History, 2 Reports, 1 Admin = 20 users, 30 s ramp, 900 s. 17:59:02-18:14:02 IST. Doctors reset and queue topped up first.

5,131 samples at 5.7 req/s, 0 errors, no Duration Assertion breaches. Window t >= 60 s: 4,806 samples, 3,992 on pass/fail endpoints and 814 on the reported Sprint 2 endpoints.

| Class (section 13.2) | Samples | p50 | p95 | p99 | max (ms) | Errors | First-third → last-third p95 |
|---|---:|---:|---:|---:|---:|---:|---|
| Reads | 3,545 | 16 | 26 | 34 | 113 | 0 | 24 → 30 ms (1.25) |
| Writes | 447 | 39 | 57 | 77 | 87 | 0 | 54 → 65 ms (1.20) |

For reference only, against the section 13.5 Azure thresholds: every latency figure is 20 to 60 times inside its limit and the error rate is 0%. The drift ratios sit at and just above the 20% band, on a base of a few tens of milliseconds:

- the increase is 5 to 40 ms, spread across unrelated endpoints (Admin, prescription writes, call-next, patient lookups), not concentrated on the ones whose data grows;
- response sizes were flat on every endpoint except `GET /api/queue/today` (75 → 77 KB);
- JMeter and the stack shared one Windows host.

This is consistent with host noise at a very low latency floor, not with growing data. On Azure, where the network floor alone is about 100 ms (section 12, SWC-127), the same absolute change would be a few percent.

### Per endpoint, window t >= 60 s

| Scope | Endpoint | n | p95 | p99 | max (ms) |
|---|---|---:|---:|---:|---:|
| Pass/fail | GET /api/notifications/today | 335 | 36 | 46 | 102 |
| Pass/fail | GET /api/notifications | 335 | 20 | 24 | 26 |
| Pass/fail | GET /api/patients/{id} | 416 | 31 | 37 | 43 |
| Pass/fail | GET /api/consultations/patient/{id} | 416 | 18 | 21 | 23 |
| Pass/fail | GET /api/vitals/patient/{id} | 416 | 16 | 17 | 18 |
| Pass/fail | GET /api/prescriptions/patient/{id} | 481 | 20 | 23 | 26 |
| Pass/fail | GET /api/reports/daily | 56 | 30 | 37 | 37 |
| Pass/fail | GET /api/reports/monthly | 56 | 18 | 21 | 21 |
| Pass/fail | GET /api/prescriptions/report/daily | 56 | 18 | 23 | 23 |
| Pass/fail | GET /api/prescriptions/report/monthly | 56 | 16 | 17 | 17 |
| Pass/fail | GET /api/users | 56 | 33 | 49 | 49 |
| Pass/fail | GET /api/audit-logs | 56 | 27 | 33 | 33 |
| Pass/fail | PUT /api/users/{id} | 56 | 35 | 54 | 54 |
| Pass/fail | GET /api/consultations/patient/{id}/latest-follow-up | 67 | 23 | 25 | 25 |
| Pass/fail | GET /api/consultations/latest-completed | 66 | 21 | 23 | 23 |
| Pass/fail | GET /api/prescriptions/pending | 613 | 20 | 22 | 113 |
| Pass/fail | GET /api/prescriptions/queue/{queueId} | 64 | 23 | 25 | 25 |
| Pass/fail | POST /api/consultations/{id}/vitals | 66 | 43 | 57 | 57 |
| Pass/fail | POST /api/consultations/{id}/complete | 66 | 57 | 86 | 86 |
| Pass/fail | POST /api/prescriptions | 65 | 74 | 87 | 87 |
| Pass/fail | POST /api/prescriptions/{id}/items | 65 | 48 | 69 | 69 |
| Pass/fail | DELETE /api/prescriptions/{id}/items/{medicineId} | 65 | 54 | 81 | 81 |
| Pass/fail | PUT /api/prescriptions/{id}/dispense | 64 | 48 | 71 | 71 |
| Sprint 2 | GET /api/queue/today | 613 | 35 | 41 | 46 |
| Sprint 2 | GET /api/queue/today/current | 67 | 28 | 36 | 36 |
| Sprint 2 | PUT /api/queue/call-next | 67 | 71 | 120 | 120 |
| Sprint 2 | POST /api/consultations | 67 | 60 | 138 | 138 |

## Related finding in the local SWC-136 suite

While building the Azure seed, `../local/seed-sprint4.ps1` was found writing `data/sprint4-run.properties` with a UTF-8 byte order mark. JMeter reads `.properties` as ISO-8859-1, so the mark renamed the first key and `reportDate` fell back to the plan default. The SWC-136 JTLs confirm every run requested `GET /api/reports/daily?date=2026-10-11` and `/api/prescriptions/report/daily?date=2026-10-11`, not the seeded day; the monthly reports were unaffected. Both seeds now write the file as ASCII, and this run requested `date=2026-10-12`.

## Limitations

- Local Docker, not Azure: no network latency, no 0.25 vCPU replicas, no Cloudflare. The comparison with Azure is about behaviour and relative cost per endpoint, not absolute numbers.
- The counted run started with 237 waiting patients after a top-up, so `GET /api/queue/today` returned about 75 KB, more than a fresh 150-patient Azure seed will.
- JMeter and the stack shared one host.

## Evidence

Retained locally (git-ignored): `results/local-smoke-swc137.jtl` and `-report/`, `results/local-load-swc137.jtl` (first attempt), `results/local-load-swc137-rerun.jtl` and `-report/`.
