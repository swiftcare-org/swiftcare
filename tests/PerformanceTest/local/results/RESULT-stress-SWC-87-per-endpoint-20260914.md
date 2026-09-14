# Performance run report - STRESS, per endpoint (SWC-87 queue polling)

Companion to `RESULT-stress-SWC-87-20260914.md` (the combined run). That run found
a breaking point at ~195 combined threads / ~334 req/s, with QueueService CPU as
the saturating resource. This doc isolates each endpoint - the other two Thread
Groups set to 0 users - to check whether that breaking point belongs to the
combined traffic or to any one endpoint alone.

## Run metadata (all three)

| Field | Value |
|---|---|
| Resource limits | `docker-compose.perf.yml` (same as the combined run, including the queueservice cap added for SWC-87) |
| Ramp / duration | 600 s / 600 s, poll interval 300 ± 300 ms (same as the combined run) |
| Data volume | 219 today-dated queue entries (same seed as the combined run) |

| Endpoint | Command |
|---|---|
| SWC-23 display | `jmeter -n -t SWC-87-queue-polling.jmx -q user.properties -JusersDisplay=150 -JusersToday=0 -JusersWaiting=0 -JrampUp=600 -Jduration=600 -JpollDelay=300 -JpollRange=300 -l results/stress-SWC-23-solo.jtl -e -o results/stress-SWC-23-solo-report` |
| SWC-20 today-queue | `jmeter -n -t SWC-87-queue-polling.jmx -q user.properties -JusersDisplay=0 -JusersToday=100 -JusersWaiting=0 -JrampUp=600 -Jduration=600 -JpollDelay=300 -JpollRange=300 -l results/stress-SWC-20-solo.jtl -e -o results/stress-SWC-20-solo-report` |
| SWC-21 waiting-pool | `jmeter -n -t SWC-87-queue-polling.jmx -q user.properties -JusersDisplay=0 -JusersToday=0 -JusersWaiting=100 -JrampUp=600 -Jduration=600 -JpollDelay=300 -JpollRange=300 -l results/stress-SWC-21-solo.jtl -e -o results/stress-SWC-21-solo-report` |

Each solo run used the same peak thread count that endpoint had in the combined
run (150 for display, 100 for the other two), so the numbers are directly
comparable to the "combined" column below.

## Results

| Endpoint | Peak threads | Samples | Errors | p50 | p95 | p99 | Throughput at peak | Breaking point reached? |
|---|---:|---:|---:|---:|---:|---:|---:|:--:|
| SWC-23 display (solo) | 150 | 97,238 | 0 (0.00%) | 7 ms | 44 ms | 84 ms | 312 req/s, still climbing | **No** |
| SWC-20 today-queue (solo) | 100 | 63,698 | 0 (0.00%) | 11 ms | 65 ms | 120 ms | 204 req/s, still climbing | **No** |
| SWC-21 waiting-pool (solo) | 100 | 65,054 | 0 (0.00%) | 9 ms | 34 ms | 68 ms | 208 req/s, still climbing | **No** |
| *Combined (for comparison)* | *350* | *143,043* | *0 (0.00%)* | *~655 ms* | *~1,090-1,110 ms* | *~1,320-1,372 ms* | *~289-334 req/s, plateaued* | ***Yes, at ~195 threads*** |

None of the three endpoints, run alone against the same capped stack at the same
peak thread count it carried in the combined run, showed a throughput plateau,
sustained p95 above 2000 ms, or any error. Throughput was still rising at the end
of every solo run - the ramp simply did not reach that endpoint's own ceiling
within its allotted concurrency.

## Server-side (QueueService CPU, the resource that saturated in the combined run)

| Endpoint (solo) | QueueService CPU late in ramp |
|---|---|
| SWC-23 display | reaches 99-104% only in the last two samples (~130-150 threads) |
| SWC-20 today-queue | reaches 96-104% only in the last two samples (~85-100 threads) |
| SWC-21 waiting-pool | reaches 94-98% only in the last two samples (~85-100 threads) |

In every solo run QueueService's capped core only approaches saturation right at
the end of the ramp - there was no sustained window at 100% CPU the way the
combined run showed for its last ~420 s. Latency and throughput stayed healthy
throughout because the ramp ended before sustained saturation could show up as a
plateau.

## Conclusion

The breaking point found in the combined run (`RESULT-stress-SWC-87-20260914.md`)
belongs to the **combined** traffic, not to any single endpoint. Each endpoint
alone, at the same peak concurrency it carried in the combined scenario, stayed
well inside the 3000 ms target with zero errors and no saturation trigger firing.
QueueService's single capped CPU core is shared by all three endpoints; it is
having all three poll it at once - not any one endpoint's own request rate - that
pushes it to sustained saturation. This confirms the combined run is the
representative scenario for capacity planning here (per the ticket's objective:
"testing them together gives an honest picture... rather than treating the public
display as if it were the only source of it"), while the per-endpoint runs confirm
none of the three has an individual defect - the shared dependency is the
constraint.
