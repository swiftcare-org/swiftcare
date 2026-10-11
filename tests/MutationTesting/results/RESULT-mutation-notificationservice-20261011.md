# Mutation Testing Result - NotificationService - 2026-10-11 (SWC-151)

NotificationService was merged after the six-service run of 2026-10-05
(`RESULT-mutation-20261005.md`), so it was added on its own with the same method and settings:
Stryker.NET 5.0.0, `Standard` mutation level, per-test coverage analysis, the same exclusions.
Baseline run before any test was added, final run after. Only test files changed in between.
No production code was changed.

## Result

| | Baseline | Final |
|---|---:|---:|
| Mutation score | 86.67% | **88.57%** |
| Covered score | 95.79% | **97.89%** |
| Killed (incl. 1 timeout) | 182 | 186 |
| Survived | 8 | 4 |
| No coverage | 20 | 20 |
| Undetected mutants | 28 | 24, all classified |
| Test cases | 197 | 201 |

- 336 mutants in total: 210 valid, 33 compile errors (stillborn), 93 ignored (excluded files and
  logging calls).
- The service ends in the `High` band (≥ 80%). Its baseline was already higher than any of the
  other six services' baselines (62.11% overall), so only four real gaps were found.

Reports: `final-20261011/` and `baseline-20261011/` (local, git-ignored), regenerated with
`run-mutation-tests.ps1 -Service NotificationService`.

## Tests added

| File | Test | Mutant killed |
|---|---|---|
| `ActivityEventConsumerEdgeCaseTests` | `StorageFailureWaitsTheRetryDelayBeforeReadingTheEventAgain` | `ActivityEventConsumer` line 119: the `Task.Delay` after a failed store removed, so a failing event is re-read in a tight loop |
| `DailyReportServiceEdgeCaseTests` | `OnlyCallEventsCountTowardsARoom` | `DailyReportService` line 71: `&&` → `\|\|`, so any event carrying a room number counts towards that room |
| `DailyReportServiceEdgeCaseTests` | `RoomsOutsideTheKnownThreeAreListedByNameIgnoringCase` | `DailyReportService` line 79: `Order()` → `OrderDescending()` for rooms outside the configured three |
| `NotificationFeedServiceEdgeCaseTests` | `EventsThatShareBothTimesAreOrderedByIdDescending` | `NotificationFeedService` line 22: the last tie-break `ThenByDescending(Id)` → `ThenBy(Id)` |

The consumer test is deterministic: the retry delay is set to 10 minutes and the test checks
that the event is not read a second time within 500 ms of the seek. With the delay removed the
second read happens immediately; with it in place the test cannot fail by chance.

## Remaining undetected mutants

All 24 are accounted for.

| Category | Survived | No coverage | Mutants |
|---|---:|---:|---|
| **Integration-only code** | 0 | 20 | `MaintenanceCommandRunner.RunAsync`/`MigrateAsync` apply EF migrations to MySQL and are verified by CI's `validate-migrations` job, as in the other five services |
| **Equivalent mutants** | 4 | 0 | See below |

| File | Line | Mutant | Why it is equivalent |
|---|---:|---|---|
| `NotificationEventParser` | 56 | `catch (JsonException)` block emptied | Stryker completes the emptied block with `return default`, which is `null`, the value the original returns |
| `NotificationEventParser` | 148 | `<=` → `<` on the diagnosis length | A trimmed diagnosis of exactly 200 characters is cut to its first 200 characters and trimmed again, which is the same string |
| `NotificationRecorder` | 18 | Duplicate pre-check removed | The unique index on `EventId` rejects the insert and the `catch` returns `Duplicate`, the same outcome (as `PrescriptionManagementService` line 40) |
| `ActivityEventConsumer` | 144 | `base.Dispose()` removed | The host always stops the worker before disposing it, and stopping already cancels the loop (as `ConsultationCompletedConsumer` line 139 in QueueService) |

## Threshold

`break` was 0 for the baseline so it could not fail. Following the rule used for the other six
services (5 points below the final score, rounded down to 5; `low` = `break`, `high` = `break` + 5):

| Service | high | low | break | Final score |
|---|---:|---:|---:|---:|
| NotificationService | 85 | 80 | 80 | 88.57% |

## CI

NotificationService is the seventh leg of the `mutation-testing` matrix in
`.github/workflows/ci.yml`. Its unit tests already ran in the `build-and-test` job, so no other
CI change was needed.
