# SwiftCare Sprint 1 — Performance Test Plan

**Jira:** SWC-67  **Tool:** Apache JMeter 5.6.x  **Target:** Sprint 1 API slice

## 1. Objective

Establish, on the local `docker-compose` environment:

1. **Baseline** — latency and throughput at expected clinic load.
2. **Headroom** — how far past expected load the system holds before latency or
   errors breach the thresholds in §5.
3. **Bottleneck** — which resource saturates first at the breaking point.

Results are for relative comparison (before/after a change) and for locating the
knee **on this environment**. They are not an absolute capacity guarantee for
production hardware.

## 2. Scope

- **In:** AuthService, PatientService, API Gateway, MySQL, Kafka — exercised only
  through the API Gateway on `:8000` (so JWT validation, header rewriting and
  YARP routing are part of what is measured).
- **Out:** the frontend (tested separately via Selenium E2E), QueueService and
  the other unbuilt services, CI integration, production-representative
  infrastructure.

## 3. Assumptions (locked before execution)

| # | Assumption |
|---|---|
| A1 | Small-clinic scale: normal ≈ 10 concurrent staff sessions, peak ≈ 20. |
| A2 | The first Load run *establishes* the baseline; later runs compare against it. |
| A3 | Login is a once-per-thread setup step, not part of the request mix (staff authenticate once per shift, not per action). |
| A4 | Create-user is excluded from the mix (admin-only onboarding, rare). |
| A5 | JWT lifetime ≈ 1 h; runs stay under that, so no mid-run re-auth is modelled. |

## 4. Workload model

Weights are by **controller execution**. Row 2 issues two HTTP requests per
execution, so by raw request count reads are > 85% of traffic.

| # | Action | Method + path | Weight | Data source |
|---|---|---|---:|---|
| — | Login | `POST /api/auth/login` | setup | `data/users.csv` |
| 1 | Search patient | `GET /api/patients/search?q={term}` | 55% | `data/search-terms.csv` |
| 2 | Open patient profile | `GET /api/patients/{id}` then `GET /api/patients/{id}/allergies` | 30% | `data/patients.csv` |
| 3 | Register patient | `POST /api/patients` | 10% | generated (unique NIC + phone per request) |
| 4 | Add allergy | `POST /api/patients/{id}/allergies` | 5% | `data/patients.csv` |

**Justification.** SWC-12 states patient search is "used dozens of times daily" —
every patient interaction begins with a lookup, and opening a profile is the
usual follow-up, so reads dominate. Registration happens only for new patients;
allergy edits are occasional. The mix is deliberately read-heavy to match a
clinic front desk, not balanced across CRUD.

**Think time:** Gaussian, 1000 ms ± 1500 ms between actions.

## 5. Pass / fail criteria (defined in advance)

**Measurement rules.** Discard the ramp-up period and the first 30 s of steady
state. Split endpoints into **reads** (search, profile, allergies) and **writes**
(register, add allergy). "Sustained" = holds for ≥ 60 s.

### 5.1 Load run — PASS requires all six

Profile: 20 users, 30 s ramp, 15 min steady state.

| Metric | Threshold |
|---|---|
| Reads p95 | ≤ 800 ms |
| Reads p99 | ≤ 1500 ms |
| Writes p95 | ≤ 1500 ms |
| Writes p99 | ≤ 3000 ms |
| Error rate (all requests) | ≤ 0.5% |
| Latency drift | last-third p95 within 20% of first-third p95 |

### 5.2 Stress run — breaking point

Profile (calibrated after the Load baseline): ramp 10 → **400** users over 10 min,
against the **CPU/memory-capped stack** (`docker-compose.perf.yml`), think-time
reduced to 300 ± 300 ms so request rate actually climbs with concurrency.

> Deviation from the pre-registered plan: the original ceiling was 80 users with
> 1000 ± 1500 ms think time. The Load run showed the uncapped system idle at
> 20 users (read p95 8 ms, 0 errors), so 80 users would not have reached a knee.
> The thresholds below are unchanged.

The **breaking point** is the first point at which any one of these holds for
≥ 60 s:

| Condition | Trigger |
|---|---|
| Aggregate p95 latency | > 2000 ms |
| Aggregate error rate | > 1% |
| Saturation | throughput (req/s) flat or falling while active threads still increasing |

**Record at that point:** active thread count, throughput (req/s), aggregate p95,
error rate, and the first resource to saturate (container CPU / memory from
`docker stats`, MySQL `Threads_connected` vs `max_connections`, etc.).

### 5.3 Rationale for the numbers

- 800 ms read p95 ≈ "a receptionist does not perceive lag" for an interactive
  lookup.
- 2000 ms / 1% are the conventional "degraded" lines for an internal
  line-of-business API.
- The drift check catches slow leaks / pool exhaustion that a point-in-time p95
  misses.

## 6. Test types

| Type | Profile | Status |
|---|---|---|
| Smoke | 1 user, 60 s | Required — gate for the rest |
| Load | 20 users / 30 s / 900 s | Required |
| Stress | 80 users / 480 s ramp / 600 s | Required |
| Spike | normal → 3× for 2 min → normal | Stretch |
| Soak | normal load, 1–2 h (watch memory, MySQL connections, GC, latency drift) | Stretch |

## 7. Environment

- Local `docker-compose`, full Sprint 1 stack.
- For attributable stress results, apply `docker-compose.perf.yml` (per-service
  CPU/memory limits) and record the limits used.
- Load generator (JMeter) and system-under-test on the same machine — acceptable
  for relative comparison; note it as a limitation.
- Observability: JMeter client-side timings + `docker stats` + service structured
  logs + MySQL `SHOW GLOBAL STATUS`. No APM in the services yet.

## 8. Deliverables

- `swiftcare-load.jmx`, `user.properties`, `seed.ps1`, `data/*.csv.example`,
  `docker-compose.perf.yml`, this plan, `README.md`.
- A filled `results/REPORT-*.md` for the Smoke, Load and Stress runs, each with
  the metrics table, a pass/fail verdict against §5, and a short analysis of the
  breaking point and first-saturating resource.
