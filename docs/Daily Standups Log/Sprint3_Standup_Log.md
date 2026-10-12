# SwiftCare: Sprint 3 Daily Stand-up Log
**Consolidated from:** Business Analyst (IT24103439), Developer (IT24103444), DevOps (IT24103431), QA (IT24103437)
**Period:** 16th September:  29th September 2026
**Sprint Goal:** Extend the workflow from consultation into treatment: doctors record vital signs and complete consultations, medical alert banners surface allergies and conditions at the point of care, and the first prescription-to-dispensing loop closes the patient's visit end to end.

---

## Sprint 3 Backlog Summary

**30 work items, all Done:** 9 user stories (24 story points), 4 bugs and 17 tasks.

### User Stories

| Story | Title | Service | SP | Status | Dev Completed | QA Approved |
|---|---|---|---|---|---|---|
| SWC-92 | View Patient Profile for Current Consultation | QueueService | 2 | Done | 16 Sep | 16 Sep |
| SWC-25 | Record Vital Signs | MedicalRecordService | 3 | Done | 18 Sep | 18 Sep |
| SWC-26 | Complete Consultation | MedicalRecordService | 3 | Done | 20 Sep | 20 Sep (conditional) |
| SWC-28 | Medical Alert Banners | MedicalRecordService | 2 | Done | 21 Sep | 21 Sep |
| SWC-38 | Auto Complete Queue Entry | QueueService | 3 | Done | 22 Sep | 22 Sep |
| SWC-29 | Create Prescription | PrescriptionService | 3 | Done | 24 Sep | 24 Sep |
| SWC-40 | Add and Remove Medicines | PrescriptionService | 2 | Done | 24 Sep | 24 Sep |
| SWC-41 | Dispense Prescription | PrescriptionService | 3 | Done | 25 Sep | 25 Sep |
| SWC-30 | View Prescription at Counter | PrescriptionService | 3 | Done | 27 Sep | 27 Sep |

### Bugs

| Bug | Title | Status | Fixed | QA Verified |
|---|---|---|---|---|
| SWC-112 | Dashboard does not show the active consultation in a fresh browser context | Done | 19 Sep | 19 Sep |
| SWC-121 | Overdue follow-up endpoint returns 500 because clinic timezone configuration is not bound | Done | 21 Sep | 21 Sep |
| SWC-122 | Consultation form allows doctors to schedule a follow-up date in the past | Done | 21 Sep | 21 Sep |
| SWC-124 | Refreshing the prescription page loses the saved prescription and blocks medicine changes | Done | 24 Sep | 25 Sep |

### Tasks

| Task | Title | Owner | Status | Completed |
|---|---|---|---|---|
| SWC-94 | Apply SWC-82 isolated-failure guard to the SWC-17 allergies call | Developer | Done | 27 Sep |
| SWC-106 | Add timeout-minutes to CI/CD workflow jobs | DevOps | Done | 16 Sep |
| SWC-107 | Replace MedicalRecordService SQL schema installer with EF Core migrations | Developer | Done | 17 Sep |
| SWC-108 | Update MedicalRecordService CI/CD and deployment to use EF Core migrations | DevOps | Done | 17 Sep |
| SWC-109 | Remove dedicated MedicalRecordService MySQL migration test project | QA | Done | 18 Sep |
| SWC-110 | Add Selenium E2E coverage for Sprint 3 clinical workflows | QA | Done | 27 Sep |
| SWC-111 | Isolate Selenium test data and enable bounded E2E parallelism | QA | Done | 18 Sep |
| SWC-120 | Update current-patient Selenium setup and add fresh-session regression coverage | QA | Done | 20 Sep |
| SWC-126 | Add local JMeter load and stress tests for Sprint 3 clinical workflow endpoints | QA | Done | 27 Sep |
| SWC-127 | Extend the deployed-environment JMeter suite with Sprint 3 clinical workflow endpoints | QA | Done | 28 Sep |
| SWC-113 | Show a specific message when login is rate-limited (429) | DevOps | Done | 18 Sep |
| SWC-114 | Integrate Cloudflare for swiftcare.me (DNS, WAF, DDoS protection, rate limiting) | DevOps | Done | 18 Sep |
| SWC-115 | Automatically roll back Container Apps to the previous image on CD smoke-test failure | DevOps | Done | 19 Sep |
| SWC-116 | Provision Azure Application Insights with Terraform and wire its connection string into CD | DevOps | Done | 20 Sep |
| SWC-117 | Add Azure Application Insights telemetry to the Gateway and backend services | DevOps | Done | 21 Sep |
| SWC-123 | Add PrescriptionService to Docker, CI, CD, and Terraform | DevOps | Done | 24 Sep |
| SWC-125 | Add secret scanning and container image scanning to CI | DevOps | Done | 26 Sep |

**Unit-test subtasks completed with their parent stories:** SWC-97 (SWC-25), SWC-98 (SWC-26), SWC-99 (SWC-28), SWC-100 (SWC-29), SWC-101 (SWC-30), SWC-102 (SWC-38), SWC-103 (SWC-40), SWC-104 (SWC-41).

**Infrastructure/DevOps milestones:** workflow job timeouts (16 Sep), MedicalRecordService migrate job (17 Sep), Cloudflare edge protection and login rate limiting (18 Sep), automatic CD rollback (19 Sep), Application Insights provisioning and service telemetry (20-21 Sep), PrescriptionService deployed to Azure (24 Sep), CI secret and container image scanning (25-26 Sep).

**Testing summary:** 569/569 unit tests passing across six services. 124 Postman API test cases across the 9 stories, all passing, with the full regression chain re-run at every sign-off. Selenium E2E suite grown from 39 to 60 passing tests, including one complete clinical journey from check-in to a dispensed prescription. JMeter Load passed all six criteria locally and on Azure; local Stress located the breaking point at about 46-62 concurrent users. 4 bugs logged and closed within the sprint; SWC-128 (Call Next deadlock under stress) and SWC-129 (record that a consultation needs no prescription) carried to the Sprint 4 backlog.

---

## 16 September 2026

**Business Analyst:**
- *Progress:* Reviewed all nine Sprint 3 stories against the SRS and the Sprint 2 handover and confirmed each met the Definition of Ready. Agreed the build order with the Developer and QA, following the clinical path from vitals to dispensing. Accepted SWC-92 after QA sign-off.
- *Challenges:* SWC-92 came from client feedback after Sprint 2 and had to be scoped quickly without widening patient-data access.
- *Decisions:* Limited SWC-92 to the read access doctors already have under SWC-12; the profile stays read-only for doctors.

**Developer:**
- *Progress:* Completed the initial implementation of **SWC-107** (EF Core migrations for MedicalRecordService, replacing the SQL schema installer). Completed **SWC-92 (View Patient Profile for Current Consultation)**: the current patient's name and queue number now open the existing patient profile.
- *Challenges:* The migration had to reproduce the existing production schema exactly, including identifiers, constraints and the stable template records.
- *Decisions:* Preserved the existing database shape and changed only schema management. Reused the existing profile route and doctor read permissions for SWC-92.

**DevOps:**
- *Progress:* Added explicit `timeout-minutes` to every CI and CD job (**SWC-106**).
- *Challenges:* Timeouts needed headroom for the two backend services planned later in the sprint.
- *Decisions:* Left the reusable quality-gate job uncapped, since it is bounded by the CI job timeouts it calls.

**QA:**
- *Progress:* Approved SWC-92 (495 tests passed). Reviewed and approved SWC-106 and the SWC-107 migrations.
- *Challenges:* The reusable-workflow job cannot take a timeout, so it had to be confirmed as intentional.
- *Decisions:* Approved SWC-106 because that job is still bounded by the timeouts inside ci.yml.

---

## 17 September 2026

**Business Analyst:**
- *Progress:* Finalised the SWC-25 data definitions (vital-sign fields, units, BMI formula) and the SWC-26 completion order, and shared both with the Developer and QA.
- *Challenges:* Unusual vital-sign values needed a clear line against invalid ones so both roles tested the same rule.
- *Decisions:* Unusual values show "Please verify this value" but can still be saved; missing, zero and negative values are rejected.

**Developer:**
- *Progress:* Addressed review feedback on **SWC-107** and merged the standardisation follow-up.
- *Challenges:* The first version handled more legacy cases than the team wanted to maintain.
- *Decisions:* Removed the custom baselining component and aligned MedicalRecordService with the migration pattern used by the other services.

**DevOps:**
- *Progress:* Moved MedicalRecordService onto the shared `--migrate` pattern, with a renamed Azure migrate job and CI validation including an idempotency re-apply check (**SWC-108**).
- *Challenges:* Local migration steps needed explicit "start MySQL and Kafka first" sequencing for all four backend services.
- *Decisions:* Deleted the old Azure schema job only after the new migrate job and smoke tests had passed.

**QA:**
- *Progress:* Approved the standardised SWC-107 migrations and the SWC-108 migrate job.
- *Challenges:* A green CI run does not prove the Azure side is ready.
- *Decisions:* Checked the GitHub environment variables and Azure resources before approving.

---

## 18 September 2026

**Business Analyst:**
- *Progress:* Accepted SWC-25. Reviewed SWC-112 against the doctor dashboard requirements.
- *Challenges:* The doctor could lose the current patient in a fresh browser session while the queue still held the patient in consultation.
- *Decisions:* Confirmed with the team that the server's queue state is the source of truth for the doctor's current patient.

**Developer:**
- *Progress:* Completed **SWC-25 (Record Vital Signs)** and SWC-97: vital-sign storage, Doctor-only API, server-side BMI, live BMI display, advisory warnings, validation and unit tests.
- *Challenges:* BMI had to show instantly in the browser while remaining authoritative on the server.
- *Decisions:* Stored BMI from the backend, rounded to two decimals. Closed the separate frontend unit-test PR (SWC-105) without merging after the team confirmed frontend unit tests are not required.

**DevOps:**
- *Progress:* Moved swiftcare.me DNS to Cloudflare with WAF, DDoS protection and a login rate limit (**SWC-114**). Added a specific rate-limited message to the login page (**SWC-113**).
- *Challenges:* DNSSEC had to be confirmed off at the old registrar before switching nameservers.
- *Decisions:* Kept mail records DNS-only, since Cloudflare does not proxy mail.

**QA:**
- *Progress:* Added Selenium tests for the current patient profile (SWC-110), enabled parallel E2E runs (**SWC-111**) and removed the MySQL-dependent migration test project (**SWC-109**). Approved SWC-25 and SWC-113. Raised **SWC-112**.
- *Challenges:* Parallel E2E tests shared today's queue and interfered with each other.
- *Decisions:* Limited parallelism to two workers and ran shared-queue tests on their own with separate data.

---

## 19 September 2026

**Developer:**
- *Progress:* Fixed **SWC-112**: QueueService now returns the doctor's current assignment, and the dashboard and consultation page recover it from the server. Started SWC-26 schema changes.
- *Challenges:* A new browser context had no stored assignment, so the dashboard showed empty while Call Next stayed blocked.
- *Decisions:* Made QueueService the source of truth and kept browser storage for authentication only.

**DevOps:**
- *Progress:* Added automatic rollback to CD when a backend deployment step fails (**SWC-115**). Started Application Insights provisioning (**SWC-116**).
- *Challenges:* Container App updates go live immediately, so a failure after several apps updated needed an explicit revert.
- *Decisions:* First-time deployments with no previous image roll forward instead of blocking.

**QA:**
- *Progress:* Approved the SWC-112 fix and the SWC-115 CD rollback.
- *Challenges:* One older SWC-92 E2E test no longer matched the fixed behaviour.
- *Decisions:* Approved the fix and took the outdated test as a follow-up (**SWC-120**).

---

## 20 September 2026

**Business Analyst:**
- *Progress:* Reviewed SWC-26 and accepted it conditionally.
- *Challenges:* Completion opened a placeholder page because the prescription form (SWC-29) was not built yet.
- *Decisions:* Accepted on the condition that SWC-29 replaces the placeholder.

**Developer:**
- *Progress:* Completed **SWC-26 (Complete Consultation)** and SWC-98: store COMPLETE and the event ID first, then publish; retries reuse the same event ID, and QueueService ignores duplicates.
- *Challenges:* A Kafka failure after the database write had to leave the record complete and the queue in consultation. CI coverage briefly fell below the threshold.
- *Decisions:* Persisted the event ID before publishing and added focused tests to restore the coverage gate.

**DevOps:**
- *Progress:* Completed Application Insights provisioning with Terraform and wired the connection string into CD for all Container Apps (**SWC-116**).
- *Challenges:* Registering the required Azure resource providers needed subscription Owner permissions.
- *Decisions:* Made the connection string optional in CD and stored it only as a GitHub Environment secret.

**QA:**
- *Progress:* Completed **SWC-120** (current-patient tests use a real queue assignment, with a fresh-session regression test). Added vital-sign Selenium tests. Conditionally approved SWC-26 (544 tests passed), including a controlled Kafka outage test.
- *Challenges:* After completion, the app navigated to a prescription placeholder page.
- *Decisions:* Gave a conditional approval, since SWC-29 replaces that page.

---

## 21 September 2026

**Business Analyst:**
- *Progress:* Refined the SWC-28 follow-up rules and accepted SWC-28 after SWC-121 and SWC-122 were fixed.
- *Challenges:* There was no existing source for follow-up data, and "overdue" needed a precise meaning.
- *Decisions:* Follow-up date and instruction are captured together on the consultation form; a follow-up due today is not overdue, and past dates cannot be entered.

**Developer:**
- *Progress:* Completed **SWC-28 (Medical Alert Banners)** and SWC-99: optional follow-up fields, an overdue follow-up endpoint, and red, amber and blue banners in order. Fixed **SWC-121** and **SWC-122**.
- *Challenges:* The configuration name did not match the options property, causing a runtime 500.
- *Decisions:* Matched the setting, added start-up validation, and compared dates against the Asia/Colombo clinic day.

**DevOps:**
- *Progress:* Rolled out Application Insights telemetry to the Gateway and all four backend services (**SWC-117**).
- *Challenges:* Telemetry could not interfere with the `--migrate` maintenance command.
- *Decisions:* Piloted on the Gateway first and verified live in the Azure Portal before rolling out.

**QA:**
- *Progress:* Tested SWC-28 with Postman and in the browser. Raised SWC-121 and SWC-122; both were fixed, retested and SWC-28 was approved.
- *Challenges:* Unit tests set the clinic time zone themselves, so the bug showed only against the running service.
- *Decisions:* Asked for a test that reads the real configuration.

---

## 22 September 2026

**Business Analyst:**
- *Progress:* Accepted SWC-38. Walked through the doctor-to-reception hand-off with QA.
- *Challenges:* A repeated completion event must not change the recorded completion time.
- *Decisions:* Confirmed duplicate events keep the original completion time.

**Developer:**
- *Progress:* Completed **SWC-38 (Auto Complete Queue Entry)** and SWC-102: queue entries move to COMPLETED with CompletedAt when the event arrives, and completed rows show the prescription action.
- *Challenges:* Kafka can redeliver events, so completion could not apply twice.
- *Decisions:* Used the processed-event ledger for idempotency and stored completion time in UTC.

**QA:**
- *Progress:* Added Selenium tests for alert banners and consultation completion. Approved SWC-38.
- *Challenges:* A test failed because the page shows "IN CONSULTATION" with a space.
- *Decisions:* Matched the text the user sees rather than the stored value.

---

## 23 September 2026

**Developer:**
- *Progress:* Implemented the main **SWC-29 (Create Prescription)** workflow and SWC-100 in the new PrescriptionService: prescription and medicine entities, one prescription per consultation, PENDING default, Gateway routes, advisory allergy warning and previous prescriptions.
- *Challenges:* First PrescriptionService workflow; leaving or refreshing the form could lose unsaved work.
- *Decisions:* Stored cross-service IDs as references only and took doctor identity from trusted Gateway headers. Began a recovery path for unfinished prescriptions.

---

## 24 September 2026

**Business Analyst:**
- *Progress:* Accepted SWC-29 and SWC-40. Linked SWC-124 to SWC-40.
- *Challenges:* Leaving or refreshing the prescription page could lose the doctor's work.
- *Decisions:* Added unfinished-prescription recovery to SWC-29 and treated SWC-124 as a defect against SWC-40's acceptance criteria.

**Developer:**
- *Progress:* Finalised **SWC-29** with prescription recovery after refresh or navigation. Completed **SWC-40 (Add and Remove Medicines)** and SWC-103. Fixed **SWC-124** so a saved PENDING prescription is restored after refresh.
- *Challenges:* Navigation state was not durable, and CI flagged a formatting issue.
- *Decisions:* Restored the prescription from the server and kept DISPENSED prescriptions read-only.

**DevOps:**
- *Progress:* Onboarded PrescriptionService into Docker, CI, CD and Terraform and verified it live in Azure (**SWC-123**).
- *Challenges:* The service's low initial coverage dropped the CI gate below 55%, and its MySQL user had never been created.
- *Decisions:* Closed the coverage gap with real tests and diagnosed the database issue from inside the private network.

**QA:**
- *Progress:* Approved SWC-29 and SWC-40. Raised **SWC-124**. Added prescription Selenium tests and started PrescriptionService in the CI E2E run. Approved the SWC-123 deployment.
- *Challenges:* The CI E2E run did not start PrescriptionService, so the new tests had no backend.
- *Decisions:* Added the service to the CI E2E setup instead of skipping the tests.

---

## 25 September 2026

**Business Analyst:**
- *Progress:* Accepted SWC-41 after the timing and queue-action issues were fixed.
- *Challenges:* Dispensing times appeared 5 h 30 min early after a reload.
- *Decisions:* Held acceptance until the times matched clinic local time and the View Prescription action was restored.

**Developer:**
- *Progress:* Completed **SWC-41 (Dispense Prescription)** and SWC-104: Receptionist-only dispensing, PENDING to DISPENSED, duplicate-dispense protection. Applied two review fixes.
- *Challenges:* Times read back from MySQL were not marked as UTC.
- *Decisions:* Normalised stored timestamps to UTC and kept the prescription action on completed queue rows.

**DevOps:**
- *Progress:* Added container image scanning (Trivy, all six images) and secret scanning (gitleaks, full commit history) to CI (**SWC-125**).
- *Challenges:* Every scan job failed immediately; the pinned action tag was not a real release.
- *Decisions:* Re-pinned to a real release and continued verifying the install rather than trusting the first green run.

**QA:**
- *Progress:* Retested the SWC-124 fix and approved SWC-41.
- *Challenges:* Found the dispensing-time shift and a missing View Prescription action.
- *Decisions:* Held approval until both were fixed and retested.

---

## 26 September 2026

**DevOps:**
- *Progress:* Root-caused and fixed the remaining scanner install failure (**SWC-125**). Verified both scan jobs with real output: no vulnerabilities on the auth image and no leaked secrets in the commit history.
- *Challenges:* An intermediate fix looked plausible but did not resolve the failure.
- *Decisions:* Confirmed against actual scan output before closing, since a misconfigured scan can pass without scanning anything.

---

## 27 September 2026

**Business Analyst:**
- *Progress:* Accepted SWC-30. Ran the full clinical journey from check-in to a dispensed prescription. Captured the client's request for doctor-created consultation templates.
- *Challenges:* A consultation that needs no prescription cannot be recorded, so the counter cannot tell "not needed" from "not written yet".
- *Decisions:* Treated this as new scope (SWC-129 for Sprint 4) and recorded the template request as a Sprint 4 story.

**Developer:**
- *Progress:* Completed **SWC-30 (View Prescription at Counter)** and SWC-101: pending list ordered oldest first, full counter view and the clear "No prescription recorded yet" state. Completed **SWC-94**, so an allergy-loading failure affects only the Allergies section.
- *Challenges:* The counter view needed data owned by three services without cross-service database access.
- *Decisions:* Combined separate Gateway responses in the frontend and applied the SWC-82 isolation pattern to allergies.

**QA:**
- *Progress:* Approved SWC-30 and SWC-94. Finished Selenium coverage (**SWC-110**, 60/60 in CI). Ran JMeter Smoke, Load and Stress locally (**SWC-126**) and Smoke and Load on Azure (**SWC-127**). Raised SWC-128.
- *Challenges:* JMeter scripts failed on Java 26, and the Cloudflare login rate limit blocked Azure seeding.
- *Decisions:* Ran JMeter on Java 21; DevOps disabled the rate limit for the test window only. Sent SWC-128 to Sprint 4, since the data stayed correct.

---

## 28 September 2026

**Business Analyst:**
- *Progress:* Confirmed all 30 Sprint 3 work items Done (24 of 24 story points). Prepared the Sprint 4 backlog and the BA report, retrospective report and review slides.
- *Challenges:* Follow-up items from several roles needed consolidating into clear backlog items.
- *Decisions:* Carried SWC-128 and SWC-129 into Sprint 4, with the client's template request and the availability-test action, each with an owner.

**QA:**
- *Progress:* Repeated the Azure Load test under the same conditions: 6 of 6 criteria passed with 0 errors (**SWC-127**). Added SWC-129 to Sprint 4. Wrote the Sprint 3 QA report and slides.
- *Challenges:* The first Azure run passed only 3 of 6 criteria during a slow phase.
- *Decisions:* Kept the first run on record and made the identical repeat the official result.

---

*End of Sprint 3 consolidated stand-up log.*
