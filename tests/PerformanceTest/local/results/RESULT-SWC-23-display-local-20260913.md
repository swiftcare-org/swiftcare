# SWC-23 - JMeter Load Result - GET /api/queue/display (Local)

**Plan:** `SWC-23-display.jmx` (tracked at `tests/PerformanceTest/local/`)  **Raw samples:** `SWC-23-display-local.jtl` (not committed - regenerate with the command below; consistent with `tests/PerformanceTest/local/.gitignore`, which excludes raw `.jtl`/`.log` and keeps only summaries)
**Environment:** Local docker-compose (`docker compose up -d --build`), commit `787db31`
**Date:** 2026-09-13, re-verified 2026-09-14 after relocation into the tracked performance suite (SWC-87)

## Why this endpoint

`GET /api/queue/display` is the only polling endpoint in SwiftCare. Every waiting-room
screen re-reads it every 5 seconds, unauthenticated, with no cache and no rate limit,
against the same MySQL instance the staff-facing queue views use. It is the first
genuinely hot-path endpoint in the system, which is why it was picked for a JMeter pass
this sprint (CLAUDE.md section 5 conditions: deployed and reachable, and
performance-sensitive - both true here).

## Profile

- 15 threads (concurrent simulated waiting-room screens), 15s ramp-up
- Each thread: GET, then a 4.5-5.5s randomized wait (approximates the frontend's real
  5000ms poll interval - see `frontend/src/pages/WaitingRoomDisplayPage.tsx`,
  `POLL_INTERVAL_MS`), 20 loops per thread
- Target (CLAUDE.md section 5): **3 second response time at 15 concurrent users**
- Assertions per sample: HTTP 200, body contains `currentRooms` and `nextQueueNumbers`,
  response time under 3000ms

## Command

```bash
jmeter -n -t tests/PerformanceTest/local/SWC-23-display.jmx \
  -Jusers=15 -JrampUp=15 -Jloops=20 -Jhost=localhost -Jport=8000 \
  -l tests/PerformanceTest/local/results/SWC-23-display-local.jtl
```

## Result

| Metric | Value |
|---|---|
| Samples | 300 |
| Errors | 0 (0.00%) |
| HTTP 200 | 300 / 300 |
| Failed assertions | 0 |
| Average | 10.9ms |
| Min | 6ms |
| Max | 386ms |
| p50 | 10ms |
| p90 | 11ms |
| p95 | 12ms |
| p99 | 13ms |

**Target met.** p99 (13ms) is roughly 230x under the 3000ms target at the specified 15
concurrent users. No error, no assertion failure. Run three times across two review
cycles with consistent results each time (2026-09-13: avg 9.5ms/p99 18ms, then avg
9.2ms/p99 17ms; 2026-09-14 re-verification after relocating the plan into
`tests/PerformanceTest/local/`: avg 10.9ms/p99 13ms, one 386ms outlier on the first
sample of the run, consistent with container JIT/connection warm-up rather than a
regression) - the figures above are from the most recent run.

## Reading this result

This is a local docker-compose environment on developer hardware, not production
infrastructure, and MySQL held only the data this sprint's QA runs put into it (a low
row count in `QueueEntries`). The result is a genuine, reproducible local baseline, not
a production capacity guarantee - consistent with the scope note in
`tests/PerformanceTest/local/TEST-PLAN.md` for the existing Sprint 1 suite.

The margin here (single-digit milliseconds against a 3-second target) is large enough
that this endpoint is not a load concern at the current data volume and this concurrency.
The number worth re-testing later is not the response time but the row count: this
endpoint runs an unindexed-by-default scan-shaped query (`Where(QueueDate == today &&
Status in (Waiting, InConsultation))`) with no `LIMIT`, so a clinic day with hundreds of
queue entries rather than a handful is the condition that would actually stress it, not
more concurrent screens. Worth another JMeter pass once the app has been run against a
realistically-sized queue table.
