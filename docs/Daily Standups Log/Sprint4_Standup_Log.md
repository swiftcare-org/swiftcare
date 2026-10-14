# SwiftCare: Sprint 4 Daily Stand-up Log
**Consolidated from:** Business Analyst (IT24103437), Developer (IT24103431), DevOps (IT24103439), QA (IT24103444)
**Period:** 30th September: 13th October 2026 (Asia/Colombo)
**Entries covered:** 27th September: 12th October 2026 (evidence cut-off 12 October)
**Sprint Goal:** Complete the clinical record and close the product backlog: doctors see patient, vital-sign and prescription history, record follow-ups, no-prescription outcomes and their own consultation templates; admins manage staff accounts and read daily and monthly reports; reception and admin follow a live activity feed; and the platform gains monitoring, code-quality and mutation-testing gates, per-service releases and separate development and production environments.

> **Basis:** Reconstructed from the four Sprint 4 role logs, which draw on Jira assignments, dated Git commits, merged GitHub pull requests, GitHub `APPROVED` events and recorded pipeline and Azure evidence. It is a work log, not a transcript of daily meetings. QA entries are dated by the original GitHub approval time. BA story-preparation dates follow the BA log's proposed sequential handover and are not verified Jira timestamps. PR merge dates can be later than the development date shown.

---

## Sprint 4 Backlog Summary

**10 user stories (25 story points), all Done**, plus bugs, corrective fixes and tasks listed below. The product backlog closes with **34 of 34 user stories Done** across four sprints.

### User Stories

| Story | Title | BA Prepared | Dev Completed | QA Approved | PR |
|---|---|---|---|---|---|
| SWC-27 | View Patient History and Vitals | 29 Sep | 30 Sep | 2 Oct | [#101](https://github.com/swiftcare-org/swiftcare/pull/101) |
| SWC-42 | View Prescription History | 30 Sep | 2 Oct | 3 Oct | [#106](https://github.com/swiftcare-org/swiftcare/pull/106) |
| SWC-39 | Record Follow Up (reminder) | 1 Oct | 4 Oct | 4 Oct | [#107](https://github.com/swiftcare-org/swiftcare/pull/107) |
| SWC-10 | Manage Users and Audit Log | 2 Oct | 6 Oct | 6 Oct | [#114](https://github.com/swiftcare-org/swiftcare/pull/114) |
| SWC-131 | Doctor-owned Consultation Templates | 3 Oct | 7 Oct | 7 Oct | [#116](https://github.com/swiftcare-org/swiftcare/pull/116) |
| SWC-129 | Record that a Consultation Needs No Prescription | 4 Oct | 7 Oct | 8 Oct | [#119](https://github.com/swiftcare-org/swiftcare/pull/119) |
| SWC-43 | Live Activity Feed | 5 Oct | 8 Oct | 8 Oct | [#125](https://github.com/swiftcare-org/swiftcare/pull/125) |
| SWC-31 | Daily Summary Report | 6 Oct | 9 Oct | 9 Oct | [#126](https://github.com/swiftcare-org/swiftcare/pull/126) |
| SWC-45 | Monthly Summary Report | 7 Oct | 10 Oct | 10 Oct | [#127](https://github.com/swiftcare-org/swiftcare/pull/127) |
| SWC-44 | Today's Notifications | 10 Oct | 11 Oct | 11 Oct | [#146](https://github.com/swiftcare-org/swiftcare/pull/146) |

### Bugs and Defects

| Item | Title | Reported | Fixed | QA Verified | PR |
|---|---|---|---|---|---|
| SWC-128 | Call Next Patient returns 500 under concurrent calls (carried from Sprint 3) | 27 Sep | 10 Oct | 10 Oct | [#128](https://github.com/swiftcare-org/swiftcare/pull/128) |
| SWC-147 / GitHub #104 | All-zero patient GUID returns 500 on history and vitals endpoints | 2 Oct | 2 Oct | 2 Oct | [#105](https://github.com/swiftcare-org/swiftcare/pull/105) |
| SWC-148 → SWC-150 / GitHub #108 | All-zero GUID returns 500 on the latest-follow-up endpoint; consolidated into invalid-GUID route handling | 4 Oct | 5 Oct | 5 Oct | [#111](https://github.com/swiftcare-org/swiftcare/pull/111) |
| GitHub #120 | API path parameters not URL-encoded | — | 7 Oct | — | [#122](https://github.com/swiftcare-org/swiftcare/pull/122) |
| GitHub #121 | Sensitive error detail in consultation-completion logs | — | 7 Oct | — | [#123](https://github.com/swiftcare-org/swiftcare/pull/123) |
| GitHub #124 | Failed queue-number allocation reported as "already queued" and check-in lost | 8 Oct | 10 Oct | 10 Oct | [#129](https://github.com/swiftcare-org/swiftcare/pull/129) |

### Corrective Fixes

| Item | Title | Fixed | QA Approved | PR |
|---|---|---|---|---|
| SWC-159 | Clinic-local dates and active assignments | 10–11 Oct | 11 Oct | [#137](https://github.com/swiftcare-org/swiftcare/pull/137) |
| SWC-160 | Original consultation completion timestamps | 10–11 Oct | 11 Oct | [#138](https://github.com/swiftcare-org/swiftcare/pull/138) |
| SWC-155 | Atomic prescription outcomes and dispensing | 11 Oct | 11 Oct | [#139](https://github.com/swiftcare-org/swiftcare/pull/139) |
| SWC-158 | Recovery of unfinished prescription visits | 11 Oct | 11 Oct | [#140](https://github.com/swiftcare-org/swiftcare/pull/140) |
| SWC-156 | Durable queue-event delivery | 11 Oct | 11 Oct | [#141](https://github.com/swiftcare-org/swiftcare/pull/141) |
| SWC-154 | Clinical visit relationship validation | 11 Oct | 11 Oct | [#142](https://github.com/swiftcare-org/swiftcare/pull/142) |
| SWC-157 | Durable session invalidation | 11 Oct | 11 Oct | [#144](https://github.com/swiftcare-org/swiftcare/pull/144) |

Together with #128 and #129, these make up the nine corrective-fix PRs approved by QA across 10–11 October.

### Tasks

| Task | Title | Owner | Status (12 Oct) | Completed | PR(s) |
|---|---|---|---|---|---|
| SWC-132 | Availability monitoring | DevOps | Done | 4 Oct | [#109](https://github.com/swiftcare-org/swiftcare/pull/109) |
| SWC-149 | SonarQube Cloud analysis in CI | DevOps | Done | 7 Oct | [#115](https://github.com/swiftcare-org/swiftcare/pull/115), [#118](https://github.com/swiftcare-org/swiftcare/pull/118) |
| SWC-153 | Mutation testing job in CI | DevOps | Merged; manual full run not yet recorded | 6 Oct | [#113](https://github.com/swiftcare-org/swiftcare/pull/113), [#112](https://github.com/swiftcare-org/swiftcare/pull/112) |
| SWC-151 | Mutation testing for all backend services (Stryker.NET) | BA | Done (92.84%) | 11 Oct | — |
| SWC-134 | Independent per-service releases | DevOps | Done, proven on Azure | 11 Oct | [#148](https://github.com/swiftcare-org/swiftcare/pull/148), [#150](https://github.com/swiftcare-org/swiftcare/pull/150) |
| SWC-135 | Sprint 4 Selenium E2E coverage | QA | Done | 11 Oct | [#149](https://github.com/swiftcare-org/swiftcare/pull/149) |
| SWC-133 | NotificationService deployment (Docker, CI, CD, Terraform) | DevOps | Done, proven on Azure | 12 Oct | [#152](https://github.com/swiftcare-org/swiftcare/pull/152) |
| SWC-161 | Development and production environments | DevOps | Done | 12 Oct | [#154](https://github.com/swiftcare-org/swiftcare/pull/154), [#155](https://github.com/swiftcare-org/swiftcare/pull/155), [#159](https://github.com/swiftcare-org/swiftcare/pull/159) |
| SWC-136 | Local JMeter performance tests | QA | Done | 12 Oct | [#156](https://github.com/swiftcare-org/swiftcare/pull/156), [#158](https://github.com/swiftcare-org/swiftcare/pull/158) |
| SWC-137 | Azure JMeter performance tests | QA | Done | 12 Oct | [#161](https://github.com/swiftcare-org/swiftcare/pull/161) |
| — | Shared frontend design system | Developer | Merged | 2 Oct | [#103](https://github.com/swiftcare-org/swiftcare/pull/103) |
| — | Patient-ID and staff-account cleanup | Developer | Merged | 11 Oct | [#145](https://github.com/swiftcare-org/swiftcare/pull/145), [#147](https://github.com/swiftcare-org/swiftcare/pull/147) |
| — | DevOps audit (budget, Terraform import helper, pinned gitleaks, security headers) | DevOps | Merged | 12 Oct | [#153](https://github.com/swiftcare-org/swiftcare/pull/153) |

**Unit-test subtasks completed with their parent stories:** SWC-139 (SWC-27), SWC-142 (SWC-42), SWC-141 (SWC-39), SWC-138 (SWC-10), SWC-146 (SWC-131), SWC-130 (SWC-129), SWC-143 (SWC-43), SWC-140 (SWC-31), SWC-145 (SWC-45), SWC-144 (SWC-44).

**Infrastructure/DevOps milestones:** availability tests and alerting from five locations (2–4 Oct), SonarQube Cloud quality gate on `develop` (6–7 Oct), mutation testing in CI (6 Oct), per-service release manifest, smoke checks and rollback (11 Oct), NotificationService live on Azure (12 Oct), separate development and production environments with a demo-window gate and monthly budget (12 Oct).

**Testing summary:** Selenium E2E suite grown from 60 to **76 passing tests** (16 new). Mutation testing covers all seven backend services at **92.84%**. SonarQube gate passed at 99.25% coverage on new code after the 7 October fix. Every story approval was backed by targeted unit tests, real API checks and CI E2E 60/60; the largest automated runs were 1,412 distinct tests (SWC-31) and 1,296/1,296 (SWC-45). **JMeter, local:** smoke 174 samples/0 failures; Call Next bursts 2/2, 10/10 and 25/25; baseline 20 users, 5,143 samples/0 failures; load 40 users, 10,260 samples/0 failures, meeting all six criteria; stress ramp toward 300 users found a capacity knee near **126 active users** (MySQL connection saturation), with 15/15 post-test integrity checks passing. **JMeter, Azure:** smoke 111 samples/0 failures; 20-user load 4,803 samples/0 errors, all six thresholds passed. Defects found and closed in-sprint: two empty-GUID HTTP 500s (#104, #108), the queue-allocation defect found by mutation testing (#124), two safety fixes (#120, #121), plus the carried SWC-128 concurrency bug.

---

## 27–29 September 2026 (before the sprint)

**Business Analyst:**
- *Progress:* Logged **SWC-128** (Call Next returns 500 under concurrent calls) with evidence on 27 September. Created **SWC-129** (record that a consultation needs no prescription) and its unit-test subtask **SWC-130**; estimated SWC-129 on 28 September and refined it on 29 September. On 29 September organised the Sprint 4 scope, created and assigned unit-test subtasks **SWC-138 to SWC-146**, and fully prepared **SWC-27** (description, acceptance criteria, data definition, data-flow diagram, mock-up and unit-test scope): consultation timeline, newest-first ordering, vital-sign history and comparison with the preceding reading.
- *Challenges:* Sprint 3 ended with a concurrency defect and no way for the counter to tell "no medicine needed" from "prescription not written yet". Patient history had to bring clinical data together while preserving service ownership and authorised access.
- *Decisions:* Carried SWC-128 into Sprint 4 as a bug and treated the no-prescription outcome as new scope. Completed each story package before its development handoff.

---

## 30 September 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-42 (View Prescription History)**: the patient-history view and the history shown while writing a prescription, with earlier records kept distinct from the current one.
- *Challenges:* Both views needed consistent prescription data and predictable handling of invalid patient identifiers.
- *Decisions:* Completed the package before development on 2 October.

**Developer:**
- *Progress:* Implemented **SWC-27 (View Patient History and Vitals)**: consultation and vital-sign history endpoints, gateway routes and authorisation, a patient-history page with an expandable consultation timeline and vital-sign trends, plus ordering, controller, service and gateway tests and workflow documentation.
- *Challenges:* History had to combine clinical data without crossing service database boundaries, show newest first, and compare each vital reading with the previous one.
- *Decisions:* Exposed history through MedicalRecordService and the API Gateway and calculated trends in the history workflow, rather than copying clinical records into PatientService.

**QA:**
- *Progress:* Prepared verification of **SWC-27 / SWC-139**: access control, history ordering and API validation. No approval recorded.
- *Challenges:* None.
- *Decisions:* Logged the completed verification against the 2 October approval.

---

## 1 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-39 (Record Follow Up)**: follow-up context, doctor identification, overdue-day value and reminder wording, with a reminder mock-up.
- *Challenges:* The reminder needed to explain the overdue follow-up clearly.
- *Decisions:* Completed the package before development on 4 October.

**Developer:**
- *Progress:* Built the shared frontend design system (tokens, components, typography, navigation, application shell) and applied it to sign-in, dashboards, patient pages, queue management, consultation and vitals forms, prescription pages and the waiting-room display. Adjusted E2E selectors and route waits. Applied service-image OS security updates.
- *Challenges:* A broad UI change touched many clinical journeys and automated browser checks.
- *Decisions:* Reused shared components across pages and updated tests only where structure and wording had intentionally changed.

**QA:**
- *Progress:* Continued patient-history preparation and planned frontend regression checks as the shared component update entered review. No approval recorded.
- *Challenges:* None.
- *Decisions:* Logged the shared UI approval on 2 October.

---

## 2 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-10 (Manage Users and Audit Log)**: account and audit data definitions, Staff Accounts and Audit Log mock-ups, user creation, password reset, activation, deactivation, active-doctor listing, permissions and audit expectations.
- *Challenges:* Account changes needed consistent state and a traceable record of admin actions.
- *Decisions:* Completed the package before development on 6 October.

**Developer:**
- *Progress:* Merged the patient-history (#101) and UI (#103) PRs. Implemented **SWC-42 (View Prescription History)**: patient and prescription-page history, daily prescription totals, gateway access rules, tests and documentation. Fixed **SWC-147** so an all-zero patient GUID returns HTTP 400 instead of 500. Refined the prescription page and shared interface after review.
- *Challenges:* Prescription history had to serve as both a longitudinal view and context while writing the current prescription; invalid IDs had to fail predictably.
- *Decisions:* Reused service-owned prescription data, added explicit ID validation and kept the in-progress prescription distinct from earlier records.

**DevOps:**
- *Progress:* Wrote the Sprint 4 DevOps procedures and runbooks. Implemented **SWC-132** in Terraform: availability tests for the website and gateway health endpoint from five locations, two failure alerts and one email action group.
- *Challenges:* Alert recipients are personal data and could not be committed, yet the configuration had to work for anyone.
- *Decisions:* Kept recipients in an ignored local input file and made URLs, locations and alert threshold variables.

**QA:**
- *Progress:* Approved **#105 (SWC-147)** at 11:39 after reporting HTTP 500 for an all-zero GUID on three history/vitals endpoints ([issue #104](https://github.com/swiftcare-org/swiftcare/issues/104)); verified 400 responses with no stack trace across valid, malformed, anonymous and forbidden-role requests (84/84 MedicalRecordService tests). Approved **#101 (SWC-27/SWC-139)** at 12:03: 20 targeted tests, 601 regression tests, API/Postman and browser checks, CI E2E 60/60. Approved **#103 (shared frontend components)** at 12:20 after a consistency and regression review.
- *Challenges:* The empty-GUID 500 surfaced during patient-history API testing.
- *Decisions:* Retested the defect fix before giving the final patient-history approval.

---

## 3 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-131 (Doctor-owned Consultation Templates)**: saving, listing, applying and deleting templates.
- *Challenges:* A doctor must only access and manage their own templates.
- *Decisions:* Included ownership and unauthorised-access cases before handoff for 7 October.

**Developer:**
- *Progress:* Merged **#106**, completing SWC-42 in `develop`.
- *Challenges:* No separate development commit recorded for this date.
- *Decisions:* The merge followed the implementation and tests authored on 2 October.

**DevOps:**
- *Progress:* Applied the reviewed Terraform plan (five monitoring resources). Both targets passed from every location. A controlled gateway URL failure fired the alert and a reviewed recovery resolved it. Removed a repeating tag drift.
- *Challenges:* The first recipient did not receive the alert email although Azure recorded the alert; three recipients were still awaiting verification, and the notification test was refused on the student subscription.
- *Decisions:* Reduced recipients to one school and one personal comparison mailbox through an exact reviewed plan; confirmed delivery to the school mailbox from the Fired and Resolved emails. Stopped the gateway after the drill.

**QA:**
- *Progress:* Approved **#106 (SWC-42/SWC-142)** at 17:30: prescription-history responses, authorisation, empty states, ordering, pending-to-dispensed workflow and frontend details. Nine real HTTP API checks, PrescriptionService (30) and Gateway (8) targeted tests, related regression and CI E2E 60/60.
- *Challenges:* None.
- *Decisions:* Prescription-history acceptance testing completed and approved.

---

## 4 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-129 (No Prescription Required)** with the reception mock-up and SWC-130 unit-test scope: an explicit no-prescription outcome and rules preventing both outcomes for one consultation. Created **SWC-151** (mutation testing for all backend services with Stryker.NET) and added it to the sprint.
- *Challenges:* None recorded.
- *Decisions:* Tracked mutation testing as its own Sprint 4 task.

**Developer:**
- *Progress:* Completed and merged **SWC-39 (Record Follow Up)**: the overdue follow-up response now includes the doctor's name and days overdue, with updated reminder wording, service tests, an aligned E2E assertion and documentation.
- *Challenges:* The reminder had to identify who recorded the follow-up and express the delay clearly.
- *Decisions:* Returned the follow-up context from the backend and formatted the reminder in the frontend.

**DevOps:**
- *Progress:* Applied a reviewed maintenance plan disabling both tests and both alerts while the demo is off (0 added, 4 changed, 0 destroyed). Reviewed the Cloudflare zone and found nothing blocking the monitored paths. Merged **SWC-132** (#109). Recorded the image-pull credential owner and expiry (21 November 2026) and prepared the first SonarQube CI job.
- *Challenges:* A stale Terraform state lease blocked the maintenance apply.
- *Decisions:* Released only that lease after confirming nobody else was running Terraform. Agreed that apps and monitoring run only in approved demo windows.

**QA:**
- *Progress:* Approved **#107 (SWC-39/SWC-141)** at 11:02: overdue, future and absent follow-up states, role restrictions, 17 targeted tests and 88 MedicalRecordService tests. Reported a second HTTP 500 for the all-zero GUID on the latest-follow-up endpoint ([issue #108 / SWC-148](https://github.com/swiftcare-org/swiftcare/issues/108)); the seven-case API run recorded six passes and one defect.
- *Challenges:* The same validation gap appeared on a further endpoint.
- *Decisions:* Approved the feature and carried the defect to a dedicated fix and retest, later consolidated as **SWC-150**.

---

## 5 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-43 (Live Activity Feed)**: check-in, patient-call and consultation-completion events, ordering, duplicate-event handling and reception/admin access. Moved **SWC-151** to In Progress: added Stryker.NET and configurations for six services, covered the gateway secret middleware in every service, killed surviving mutants in PrescriptionService, QueueService, MedicalRecordService, AuthService, PatientService and ApiGateway, and documented baseline and final results.
- *Challenges:* The baseline run left surviving mutants in every service.
- *Decisions:* Added targeted tests for each surviving mutant rather than lowering thresholds.

**Developer:**
- *Progress:* Fixed **SWC-150** in MedicalRecordService and PrescriptionService and merged **#111**: all-zero GUID route values now return HTTP 400, with tests.
- *Challenges:* Invalid identifiers were reaching code paths that produced server errors.
- *Decisions:* Validated route IDs at the request boundary and covered the behaviour in both services.

**QA:**
- *Progress:* Approved **#111 (SWC-150)** at 07:51: all nine empty-GUID route checks plus anonymous and forbidden-role cases, **11/11 API checks**. Confirmed no stack traces or internal header exposure; MedicalRecordService 93/93, PrescriptionService 72/72, CI E2E 60/60.
- *Challenges:* None.
- *Decisions:* Second validation defect fixed and independently retested.

---

## 6 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-31 (Daily Summary Report)**: daily totals, diagnosis information, clinic-day boundaries and authorised access. Created **SWC-153** (mutation testing job in CI) and linked it to SWC-151. Made the Stryker thresholds valid for the raised break values. Updated source-map-js to clear a high npm audit finding.
- *Challenges:* None recorded.
- *Decisions:* Ran mutation testing in CI as a separate task so the check runs on every change.

**Developer:**
- *Progress:* Implemented and merged **SWC-10 (Manage Users and Audit Log)**: account-management data and endpoints, active-doctors list, creation audit entries, password reset, activation and deactivation, gateway routes and authorisation, Staff Accounts and Audit Log pages, unit tests and documentation.
- *Challenges:* Admin actions needed role checks and an auditable record while account state stayed consistent across the UI and AuthService.
- *Decisions:* Kept account and audit ownership in AuthService and routed admin operations through the gateway.

**DevOps:**
- *Progress:* Added mutation testing to CI (**SWC-153**): a job per service, changed-code runs on PRs, a manual trigger for full runs, score summaries and report artifacts. Rebuilt **SWC-149** for SonarQube Cloud Free with coverage export, a quality-gate job and documentation; PR #115 passed all 25 checks on its first run.
- *Challenges:* The Free plan analyses only one branch and PRs into it.
- *Decisions:* Analysed `develop` so the gate checks PRs before code reaches `main`. Reused the job's Java 21.

**QA:**
- *Progress:* Approved **#114 (SWC-10/SWC-138)** at 23:44: account creation and editing, password reset, deactivation and reactivation, role restrictions and audit-log entries and order. **34/34 real API checks**, AuthService/Gateway regression, browser workflows and CI E2E 60/60.
- *Challenges:* None.
- *Decisions:* Staff-account and audit-log workflows accepted.

---

## 7 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-45 (Monthly Summary Report)**: monthly service totals, weekly breakdowns, date boundaries and report access.
- *Challenges:* Weekly breakdowns had to stay consistent with the month's totals.
- *Decisions:* Completed the package before development on 10 October.

**Developer:**
- *Progress:* Completed **SWC-131** (doctors save, list, use and remove their own templates, with ownership checks and gateway tests). Implemented **SWC-129**: a completed consultation can record that no prescription is needed, shown to reception, with rules preventing both outcomes for one consultation. Fixed URL encoding of API path parameters (**GitHub #120**) and reduced sensitive error detail in completion logs (**GitHub #121**).
- *Challenges:* Templates had to remain doctor-owned, and the prescription workflow needed an alternative outcome without a fake prescription record.
- *Decisions:* Stored no-prescription decisions separately, enforced outcome rules in PrescriptionService and used trusted gateway identity for ownership checks.

**DevOps:**
- *Progress:* Answered the QA review of SWC-149 with a fuller README and a controlled gate failure on throwaway PR #117 (0.0% coverage on 24 new lines; closed unmerged). Merged #115, then fixed a failing `develop` gate with #118 (99.25% coverage on 95 new lines). Triaged all 347 open findings read-only. **SWC-149** confirmed Done. Analysed SWC-134 and measured existing CD run times.
- *Challenges:* After #116 merged, the gate failed at 38.8% because SonarQube counts files without a coverage report as 0%, and that PR had never run as a PR analysis.
- *Decisions:* Excluded the frontend and database-only code from coverage only (still scanned for bugs and vulnerabilities). Recommended making the SonarQube check required on `develop`.

**QA:**
- *Progress:* Approved **#115 (SWC-149)** at 14:25 after reviewing configuration, gate behaviour, security and documentation. Approved **#116 (SWC-131/SWC-146)** at 16:57: template ownership and privacy, grouping, editing, validation, removal and built-in template protection; **47/47 API checks**, 273 MedicalRecordService tests, Gateway regression, CI E2E 60/60.
- *Challenges:* The quality gate's failure behaviour needed proof, not just configuration.
- *Decisions:* Accepted the deliberate failing-gate experiment (closed without merge) as that proof.

---

## 8 October 2026

**Business Analyst:**
- *Progress:* Reviewed the unit-test scope for the handed-over stories. Reported **GitHub #124**: a failed queue-number allocation was reported as "already queued" and the check-in was lost.
- *Challenges:* The defect was found through mutation testing, not a failing feature test.
- *Decisions:* Each handed-over story keeps a unit-test subtask naming behaviour and expected results. Tracked the defect as #124.

**Developer:**
- *Progress:* Built and merged **SWC-43 (Live Activity Feed)**: NotificationService persistence and Kafka consumption for check-ins, patient calls and completions; feed API and reception/admin page; gateway protection; startup, consumer, ordering and role tests; CI unit-test execution and documentation. Merged the no-prescription and safety-fix PRs.
- *Challenges:* Kafka can redeliver events, and feed access must be role-restricted.
- *Decisions:* Recorded events once by event ID and checked roles at gateway and service boundaries. Kept NotificationService deployment as separate SWC-133 work.

**QA:**
- *Progress:* Approved **#119 (SWC-129/SWC-130)** at 00:53: no-prescription workflow, **19/19 real API checks** and browser scenarios. Approved **#125 (SWC-43/SWC-143)** at 19:15: activity events, Kafka EventId idempotency, Receptionist/Admin feed and ~10-second polling; 103 NotificationService tests, Newman 17/17 requests and 27/27 assertions, CI E2E 60/60.
- *Challenges:* One broader API scenario was not run in the initial matrix, and the feed needed Gateway integration not yet in the shared stack.
- *Decisions:* Did not count the unrun scenario as a pass; used a local-only Gateway integration for feed verification.

---

## 9 October 2026

**Business Analyst:**
- *Progress:* Reviewed the prepared daily and monthly reporting requirements for consistency: clinic-day boundaries, totals and access rules.
- *Challenges:* None recorded.
- *Decisions:* Kept SWC-45 ready for development on 10 October.

**Developer:**
- *Progress:* Implemented and merged **SWC-31 (Daily Summary Report)**: published and stored the consultation diagnosis for reporting, added the daily summary API and admin page, routed the feed and report through the gateway, and added count, date-boundary, authorisation and persistence tests plus documentation.
- *Challenges:* The report needed consistent clinic-day boundaries and access to the diagnosis.
- *Decisions:* Carried the diagnosis in the consultation-completed event and used the clinic calendar for daily totals.

**QA:**
- *Progress:* Approved **#126 (SWC-31/SWC-140)** at 20:05: daily totals, API validation, Admin frontend and graceful behaviour when downstream services were unavailable; **19 real API cases** and **1,412 distinct automated tests**. Continued focused activity-feed regression.
- *Challenges:* Environment-isolation failures affected the first automated run.
- *Decisions:* Corrected and reran them; documented Gateway integration dependencies without changing shared QA infrastructure.

---

## 10 October 2026

**Business Analyst:**
- *Progress:* Fully prepared **SWC-44 (Today's Notifications)**: filtering by the clinic-local day and permitted user access. This completed all ten story packages.
- *Challenges:* "Today" must use the clinic date, not a server date or delayed delivery time.
- *Decisions:* Completed the package before development on 11 October.

**Developer:**
- *Progress:* Implemented and merged **SWC-45 (Monthly Summary Report)**: monthly totals, weekly breakdown, admin page, gateway routes, tests and documentation. Fixed **SWC-128** by retrying call-next lock conflicts with a pause, and **GitHub #124** so failed allocation retries instead of reporting "already queued". Began **SWC-159** and **SWC-160** with regression tests.
- *Challenges:* Concurrent calls and allocation can collide, and events delivered after midnight can land on the wrong clinic day if their original time is lost.
- *Decisions:* Retried transient queue conflicts, distinguished true duplicates from failed allocations, and preserved original clinical times.

**QA:**
- *Progress:* Approved **#127 (SWC-45/SWC-145)** at 18:15: monthly totals, weekly breakdowns, new/returning patients, top diagnosis, empty states, downstream failures and daily-report regression; **1,296/1,296 automated tests**, 22 real API checks and browser acceptance. Approved **#128 (SWC-128)** and **#129 (check-in consistency, #124)** at 22:23, reviewing retry, duplicate-assignment and idempotency safeguards.
- *Challenges:* Concurrency fixes needed load evidence beyond unit tests.
- *Decisions:* Covered them with SWC-136 Call Next bursts (2/2, 10/10, 25/25). These were the first two of nine corrective-fix approvals.

---

## 11 October 2026

**Business Analyst:**
- *Progress:* Added NotificationService to mutation testing, covered its gaps, set its threshold and marked **SWC-151** Done. Documented when mutation testing runs and who owns it, covered the gaps from the full seven-service run in ApiGateway, AuthService, PatientService, PrescriptionService and QueueService, and raised thresholds: final score **92.84%**. Set the reporter field on Sprint 4 items. Reviewed all ten story packages against delivered behaviour. Marked **SWC-135** Done.
- *Challenges:* The final review had to catch any difference between prepared requirements and delivered behaviour.
- *Decisions:* Used the delivered implementation to validate the story documentation and record final clarifications.

**Developer:**
- *Progress:* Finished and merged **SWC-159** and **SWC-160**. Implemented and merged **SWC-154–158**: visit relationship validation, atomic prescription outcomes and dispensing under concurrency, durable queue-event delivery, session invalidation after account changes and logout, and recovery of older unfinished prescription work, with MySQL race, authorisation, retry and boundary tests. Completed **SWC-44** with clinic-day filtering and access tests. Hid internal patient and prescription identifiers and clarified completed check-ins and staff-account behaviour (#145, #147).
- *Challenges:* Cross-service writes, concurrency and delayed events let individually valid requests produce inconsistent state; session changes had to survive a gateway restart.
- *Decisions:* Validated authoritative visit links before writes, used database-backed concurrency and event-delivery safeguards, persisted revocation, and filtered today's feed by clinic-local day. Developer work for the sprint concluded.

**DevOps:**
- *Progress:* Implemented **SWC-134**: release manifest, change selection by comparing each app's running commit, per-service deploy, smoke and rollback scripts, and a rebuilt `cd.yml` (#148, #150). Ran five Azure scenarios (plan only, release all, changed-only, manual single service, rollback drill), all passing after one fix. Started **SWC-133** (Dockerfile, Compose, CI jobs, Terraform database, CD entry), verified locally.
- *Challenges:* The first release-all run failed before any Azure change because a called workflow does not receive Environment secrets; the old smoke test used a token that gateway session validation now rejects.
- *Decisions:* Ran service jobs directly in `cd.yml`, switched smoke checks to sign in with two synthetic accounts, and documented that rollback restores images only.

**QA:**
- *Progress:* Approved the remaining seven corrective-fix PRs between 08:30 and 08:33: #137 (SWC-159), #138 (SWC-160), #139 (SWC-155), #140 (SWC-158), #141 (SWC-156), #142 (SWC-154), #144 (SWC-157). Approved **#146 (SWC-44/SWC-144)** at 08:31: Today/All Activity filtering, authorisation and empty states; 21/21 targeted, 560 regression, 15/15 API/integration, Chrome 12/12 scenarios and 20/20 assertions, CI E2E 60/60. Delivered **#149 (SWC-135)**, merged 19:05: 16/16 new Selenium tests, **76/76 full E2E**.
- *Challenges:* A Dockerfile/manifest issue broke the Selenium CI run.
- *Decisions:* Resolved it and confirmed the subsequent Actions run succeeded. Dated each approval by its original GitHub approval, not later review-comment edits.

---

## 12 October 2026

**DevOps:**
- *Progress:* Completed **SWC-133** on Azure (database, scoped SQL user, first release, gateway release with all-service smoke check; #152). Merged the DevOps audit (#153): monthly budget, complete Terraform import helper, pinned gitleaks image and frontend security headers. Removed Kafka and NAT between windows after a cost review. Delivered **SWC-161** (#154, #155, #159): two environments in Terraform, a demo-window gate in CD and the production platform. Released current `develop` to the development environment for QA load testing (CD #51, all seven apps Running).
- *Challenges:* A new service cannot pass its smoke check through a gateway that does not yet know it. Azure allowed only one Container Apps environment per subscription, blocking a separate production environment twice. Kafka and NAT had cost about USD 54.50 in twelve days while apps were stopped.
- *Decisions:* Ran a first-time service's smoke check in the gateway job. Let production share the Container Apps environment while keeping its own network, database server, secrets, identity and site. Added a budget and kept messaging off outside approved windows.

**QA:**
- *Progress:* Delivered **SWC-136** local JMeter work (#156 merged 13:27, #158 merged 14:40): smoke 174 samples/0 failures; Call Next bursts 2/2, 10/10, 25/25; baseline 20 users, 5,143/0; load 40 users, 10,260/0, meeting all six criteria; 600-second stress ramp toward 300 users on a second host. Delivered **SWC-137** Azure JMeter work (#161 merged 19:33): smoke 111/0; 20-user load 4,803 samples/0 errors, all six thresholds passed.
- *Challenges:* Stress testing found a capacity knee near **126 active users**, driven by MySQL connection saturation.
- *Decisions:* Recorded the knee as a performance finding (15/15 post-test integrity checks passed) and made no claim that 300-user thresholds passed. These dates are delivery/merge dates; SWC-136 and SWC-137 were reviewed by IT24103437.

**Developer:**
- *Progress:* No work planned for 12–13 October, as confirmed by the Developer.

---

## 13 October 2026

- Final sprint day; the sprint ends at 8:00 PM Asia/Colombo. No activity is recorded here as of the 12 October evidence cut-off.

---

## Sprint 4 Handover

- All 10 Sprint 4 user stories (25 story points) are Done, each with a data definition, data-flow diagram and mock-up in Jira. The product backlog closes at 34 of 34 user stories Done across four sprints.
- Mutation testing covers all seven backend services at 92.84% (SWC-151). **SWC-153** is merged and its PR run passed all seven mutation legs; one manual full run is still to be recorded.
- The development environment stays open until QA finishes; afterwards stop the apps, remove messaging, set the window variable to false and read everything back.
- Open items for the next owner: rotate the image-pull token before **21 November 2026**, apply the Cloudflare TLS settings, run a database restore drill, review the unused GitHub Environments, and address the ~126-user capacity knee (MySQL connection saturation).

---

*End of Sprint 4 consolidated stand-up log.*
