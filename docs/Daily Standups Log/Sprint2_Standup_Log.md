# SwiftCare: Sprint 2 Daily Stand-up Log
**Consolidated from:** Developer (IT24103437), DevOps (IT24103444), QA (IT24103439)
**Period:** 2nd September - 15th September 2026

---

## Sprint 2 Story Summary

| Story | Title | Status | Dev Completed | QA Approved |
|---|---|---|---|---|
| SWC-13 | View and Update Patient Profile | Merged | 2 Sep | 3 Sep |
| SWC-15 | Check In Returning Patient | Merged | 3 Sep | 5 Sep |
| SWC-18 | Manage Chronic Conditions | Merged | 6 Sep | 7 Sep |
| SWC-81 | Bug: diagnosed-date validation used UTC | Fixed | 6 Sep | 7 Sep |
| SWC-82 | Bug: chronic-conditions failure crashed profile page | Fixed | 6 Sep | 7 Sep |
| SWC-20 | View Full Queue Today | Merged | 7 Sep | 7 Sep |
| SWC-21 | Doctor Views Shared Waiting Pool | Merged | 7 Sep | 8 Sep |
| SWC-22 | Call Next Patient | Merged | 8 Sep | 9 Sep |
| SWC-23 | Public Waiting Room Display | Merged | 10 Sep | 13 Sep |
| SWC-83 | Configure Internal HTTP Ingress for QueueService | Merged | - | 13 Sep |
| SWC-24 | Create Consultation Record | Merged | 14 Sep | 13 Sep |
| SWC-84 | Keep One Replica Running for Backend Apps | Merged | - | 14 Sep |
| SWC-90 | Fix Missing Services in CI E2E Environment | Merged | - | 14 Sep |
| SWC-91 | Add MedicalRecordService to Docker, CI, and Azure | Merged | - | 14 Sep (approved with conditions) |
| GitHub #48 | Consultation endpoint accepts unvalidated queueId/patientId | Closed, accepted behaviour | - | 13 Sep |

**Infrastructure/DevOps milestones:** Azure and local environment checks (7 Sep), existing-system baseline deployment (8 Sep), QueueService internal ingress on port 5003 (13 Sep), backend replica policy, CI E2E repair, and full MedicalRecordService integration on port 5004 (14 Sep).

**Testing summary:** 118 developer xUnit test methods added (153 executable cases). QA authored 8 story Postman collections (219 requests) and ran them against full regression on every test day. Selenium E2E suite grown from 22 to 39 passing tests. 3 JMeter rounds executed (local Load/Stress plus an Azure extension), all thresholds passing. 3 defects logged (SWC-81, SWC-82, GitHub #48); 2 fixed and retested within the sprint, 1 reviewed and closed as accepted behaviour.

---

## 2 September 2026

**Developer -**
- *Progress:* Completed the main implementation of **SWC-13 (View and Update Patient Profile)**. Added patient-profile update handling for address, phone, and blood group; protected NIC and date of birth from modification; added the current-day queue-status lookup; configured Gateway routes and authorization; built the receptionist profile view/edit workflow with calculated age, allergies, conditions, and queue status. 26 test methods, 36 executable cases.
- *Challenges:* Had to combine patient-owned data with queue status without moving queue ownership into PatientService. Jira's examples used integer identifiers, while the project's established model used GUIDs.
- *Decisions:* Retained the project's GUID identifiers instead of switching to integers. Kept NIC and date of birth immutable at both the request-contract and UI levels. Limited editable fields to address, phone, and blood group as required by the story.

---

## 3 September 2026

**Developer -**
- *Progress:* Completed **SWC-15 (Check In Returning Patient)**. Added the returning-patient check-in endpoint, published patient-checked-in with isNewPatient set to false, connected the profile's Check In action, and displayed the queue number once QueueService assigned it. 13 test methods, 15 executable cases.
- *Challenges:* Queue creation is asynchronous, so PatientService could not return the assigned queue number immediately after publishing the event. Review also flagged weak handling of empty correlation IDs and authorization failures during retries.
- *Decisions:* Used a bounded post-check-in queue-status lookup instead of permanent polling. Preserved correlation IDs across Kafka with a fallback for blank values. Allowed transient assignment delays to retry while surfacing authentication and authorization failures immediately.

**DevOps -**
- *Progress:* Reviewed the Sprint 2 DevOps scope and the existing Sprint 1 deployment architecture. Planned the work for QueueService internal networking, minimum backend replicas, CI E2E service startup, and MedicalRecordService deployment.
- *Challenges:* Sprint 2 work had to extend the existing infrastructure without breaking the working Sprint 1 environment.
- *Decisions:* Reuse the existing architecture and implement each task through Jira-linked branches, keeping internal services behind the API Gateway.

**QA -**
- *Progress:* Tested SWC-13 on build 67b5072. Authored the SWC-13 Postman collection (25 requests) and ran it with the setup and regression collections: 231 of 231 assertions passing. Re-ran PatientService, QueueService, and ApiGateway unit suites: 256 passing.
- *Challenges:* The stack was serving stale images and had to be rebuilt. AC2's "not checked in today" state cannot be reached for a freshly registered patient, since registration itself queues them.
- *Decisions:* Rebuilt with docker compose down and up --build before recording results. Deferred the Selenium-dependent SWC-13 cases to a batched browser run once the sprint's PatientService stories had all landed.

---

## 5 September 2026

**QA -**
- *Progress:* Tested SWC-15 on PR #37 at commit c51fcaa. All 3 acceptance criteria pass. 79 Postman assertions passed. Re-ran unit suites independently. Confirmed AC3 by a direct row count after a duplicate check-in.
- *Challenges:* An earlier run reported ten failures that were entirely environmental, from containers running images built before the endpoint was committed. The frontend dev server was not running, so two presentational states could not be observed in the DOM.
- *Decisions:* Rebuilt with --build, which cleared all ten failures. Marked the two presentational items Not verified rather than passing them by inference. No defect was raised against this story.

---

## 6 September 2026

**Developer -**
- *Progress:* Completed **SWC-18 (Manage Chronic Conditions)** — persistence model and migration, add/view/remove APIs, Gateway authorization, and the patient-profile condition form and list, with future-date validation, removal confirmation, empty-state handling, and alert presentation. 20 test methods, 26 executable cases. Also corrected **SWC-81** and **SWC-82** after review.
- *Challenges:* UTC-based diagnosed-date validation could reject the clinic's local current date between midnight and 05:30. A failed conditions request could also take down the rest of an otherwise valid patient profile.
- *Decisions:* Evaluated diagnosed dates against the configured Asia/Colombo clinic day. Isolated conditions loading so a local failure shows a section-level message instead of crashing the whole profile. Kept condition writes Receptionist-only while keeping the rest of the profile visible to authorized users.

**QA -**
- *Progress:* Reviewed the SWC-18 diff on PR #38 at commit 992a351 ahead of the test pass. Logged **SWC-81** (diagnosed-date validation uses UTC instead of clinic-local time) and **SWC-82** (a failing chronic-conditions request takes down the whole profile page).
- *Challenges:* Both findings came from a code-path trace rather than a runtime reproduction. SWC-82 is not purely SWC-18's, since the SWC-17 allergies call shares the same missing guard.
- *Decisions:* Marked both as code-review findings. Rated SWC-81 Low priority and SWC-82 Medium priority. Recommended one fix covering both the allergies and conditions calls.

---

## 7 September 2026

**Developer -**
- *Progress:* Completed **SWC-20 (View Full Queue Today)** and **SWC-21 (Doctor Views Shared Waiting Pool)**. Extended QueueService's queue-entry model with the fields required for operational display, added the current-day full-queue and waiting-pool queries, configured Gateway routes, and built the receptionist queue-management and doctor waiting-pool interfaces, both polling every 5 seconds and ordered by queue number. 22 test methods, 29 executable cases.
- *Challenges:* QueueService intentionally stores only PatientId, but both staff screens need patient names. Prescription status also depends on a PrescriptionService contract that does not exist yet. Numeric queue ordering had to stay correct beyond Q-999.
- *Decisions:* Resolved patient names through PatientService's existing profile endpoint in the frontend, caching successful lookups between polls, avoiding copied personal data and backend coupling. Kept prescription status neutral until PrescriptionService provides its endpoint. Ordered queue numbers by length then ordinal value to preserve numeric order beyond three digits.

**DevOps -**
- *Progress:* Set up and checked the Azure and local DevOps environment needed for Sprint 2, including Azure CLI access, subscription and resource configuration, Docker, Terraform, and the existing private infrastructure.
- *Challenges:* Several existing Azure resources and student-subscription limitations needed checking before deployment work.
- *Decisions:* Use the existing SwiftCare Azure environment rather than rebuilding it, and verify configuration before making deployment changes.

**QA -**
- *Progress:* Retested SWC-18 on fix commit dad9e3b and approved PR #38: 14 of 14 acceptance criteria pass, 245 assertions, zero failures. Tested and approved SWC-20 on PR #41 at commit 5bb5300: 4 of 4 acceptance criteria, 188 assertions.
- *Challenges:* SWC-20's empty-queue check self-skipped because the shared queue was not empty. Two environment issues had to be cleared first: a stale ApiGateway container, then an unexplained 500 whose exact cause was never pinned down.
- *Decisions:* Verified both SWC-18 fixes directly rather than by inference, and re-ran the SWC-17 allergy browser suite as regression. Cleared today's synthetic queue rows, with agreement, to reach SWC-20's genuine empty-state precondition. Ratified the absent prescription status as correct in-scope behaviour for SWC-20.

---

## 8 September 2026

**Developer -**
- *Progress:* Completed **SWC-22 (Call Next Patient)**. Added queue-assignment fields and migration support, first-waiting-patient selection, assigned the authenticated doctor's ID, name, and room, changed the queue state to IN_CONSULTATION, recorded CalledAt, and published patient-called. Added the Doctor-only Gateway route and enabled the dashboard's Call Next action and current-patient panel. 17 test methods, 20 executable cases.
- *Challenges:* Concurrent calls had to avoid assigning the same patient twice, and a doctor or room already handling a patient had to be blocked. Manual verification also showed the current-patient panel disappeared after a dashboard refresh.
- *Decisions:* Performed selection and assignment inside a database transaction, ordered candidates by queue number, and enforced the doctor/room occupancy rule before assignment. Used only Gateway-forwarded identity instead of client-supplied doctor fields. Restored the current-patient panel per doctor and clinic day using session storage, keeping queue state authoritative in QueueService.

**DevOps -**
- *Progress:* Deployed the existing SwiftCare project to confirm the current CI/CD and Azure setup was working before adding Sprint 2 changes. Checked the existing backend services and deployment flow as a baseline.
- *Challenges:* A stable baseline was needed so later failures could be separated from pre-existing deployment problems.
- *Decisions:* Verify the existing system first, then add Sprint 2 changes incrementally instead of changing multiple deployment areas at once.

**QA -**
- *Progress:* Tested and approved SWC-21 on PR #43 at commit b4a4383. All 3 acceptance criteria pass, 259 assertions. Confirmed the shared pool, ascending order, and 5-second poll cadence live in the browser.
- *Challenges:* AC2's "called patients disappear from the pool" half could not be exercised end to end, since no shipped API moved a patient out of WAITING until SWC-22 landed.
- *Decisions:* Recorded that half as covered at the unit level by SWC-77, to be re-verified live once SWC-22 shipped. Confirmed the empty state by stubbing the response at the network layer rather than deleting shared data.

---

## 9 September 2026

**QA -**
- *Progress:* Tested and approved SWC-22 on PR #44 at commit 5f0b313. 5 of 5 acceptance criteria pass, 272 assertions. Independently reproduced all 425 unit tests from the PR's validation section. Consumed the patient-called Kafka topic directly and confirmed no PHI in any message.
- *Challenges:* The shared environment was never empty when the empty-pool probe ran, so two checks self-skipped. A second browser context was needed to test the case where the client has no memory of being occupied.
- *Decisions:* Accepted the self-skips as honest collection behaviour. Confirmed the trust boundary adversarially: forged request bodies, forged headers, and a direct QueueService call were all refused. Flagged, not as a defect, that there is still no way to end a consultation.

---

## 10 September 2026

**Developer -**
- *Progress:* Completed **SWC-23 (Public Waiting Room Display)**. Added the anonymous display endpoint, a privacy-specific response with current room mappings and the next 3 waiting numbers, the Gateway's anonymous route, and a responsive public display refreshing every 5 seconds. 5 executable tests covering anonymous access, room mappings, waiting order, and absence of personal information.
- *Challenges:* The public endpoint had to stay useful without exposing patient or doctor information, and work without the authenticated application shell.
- *Decisions:* Projected only roomNumber and queueNumber from QueueService and verified the serialized response shape in tests. Added a standalone public route ahead of protected application routing, and displayed only queue identifiers in every UI state.

---

## 13 September 2026

**Developer -**
- *Progress:* Implemented **SWC-24 (Create Consultation Record)**. Scaffolded MedicalRecordService, created its consultation and template schema, implemented the required ADO.NET repository, exposed template and consultation-creation endpoints, configured Doctor-only Gateway routing, and built the consultation form. Template selection pre-fills symptoms, examination findings, and notes; doctor identity and room come from trusted Gateway headers; consultation time is generated by the service. 15 test methods, 22 executable cases.
- *Challenges:* This was the first implemented MedicalRecordService workflow and required a full service boundary, schema, API, and frontend path. Manual testing revealed MySqlConnector materialized template identifiers as Guid, causing the template endpoint to return 500 when read as strings.
- *Decisions:* Followed the story's explicit ADO.NET requirement with parameterized commands. Enforced required symptoms and diagnosis on both client and server, restricted one consultation per queue entry with a database constraint, and snapshotted the selected template name on the record. Replaced string-based identifier reading with typed GetGuid() access and added four idempotent starter templates.

**DevOps -**
- *Progress:* Completed **SWC-83**. Updated the deployment workflow to enable internal QueueService HTTP ingress on port 5003, preserved Kafka consumer behavior, updated related documentation, and merged PR #46.
- *Challenges:* QueueService needed HTTP reachability for internal communication without being exposed publicly.
- *Decisions:* Used internal-only ingress, kept external access disabled, retained gateway-secret protection, and verified Kafka behavior after the networking change.

**QA -**
- *Progress:* Tested and approved three pull requests. SWC-23, PR #45 at commit 787db31: 5 of 5 acceptance criteria, 275 assertions, JMeter pass at 15 concurrent screens. SWC-24, PR #47 at commit 120d7d4: 5 of 5 acceptance criteria, 61 assertions, 3 Selenium journeys passing. SWC-83, PR #46 at commit b4153e0: 8 of 9 acceptance criteria pass, one not run.
- *Challenges:* SWC-24's endpoint accepts any queueId and patientId, including fabricated values, and returned 201 Created for a made-up pair. MedicalRecordService needed a manual start and schema apply before anything could be tested. SWC-83's AC1 could not be exercised without deleting a running Azure service.
- *Decisions:* Raised GitHub issue #48 against SWC-24 for the team to rule on. The review closed it with no code change, since the endpoint is Doctor-only, identity comes from trusted Gateway headers, and a fix would need cross-service validation outside the agreed scope; recorded as accepted behaviour rather than an open blocker. Used a local, uncommitted Compose override to reach MedicalRecordService and flagged the setup gap for DevOps. Marked SWC-83's AC1 as Not run rather than passing it on inspection.

---

## 14 September 2026

**Developer -**
- *Progress:* Finalized SWC-24 after integration and manual verification. Confirmed consultation-template loading, editable template prefill, required-field validation, trusted doctor identity, successful permanent record creation, duplicate queue protection, and clear success and error states. Reviewed the full Sprint 2 implementation and prepared its technical evidence and demonstration flow.
- *Challenges:* The complete workflow spans PatientService, QueueService, Kafka, the API Gateway, the frontend, and MedicalRecordService, so final verification had to distinguish new story coverage from the larger regression suites.
- *Decisions:* Reported the 153 executable xUnit cases introduced by the eight stories separately from the complete regression totals. Retained explicit ownership boundaries: patient demographics remain in PatientService, queue state remains in QueueService, consultation records remain in MedicalRecordService.

**DevOps -**
- *Progress:* Completed the remaining Sprint 2 DevOps stories. For SWC-84, configured backend applications to keep minimum and maximum replicas at one, documented the shutdown behavior, and merged PR #49. For SWC-91, added the MedicalRecordService schema runner, Docker Compose startup, CI integration, database management, Azure deployment, and deployment documentation, then merged PR #51. For SWC-90, added the missing services to the CI E2E environment, documented CI E2E startup, and merged PR #52.
- *Challenges:* The final day's work touched multiple deployment areas at once: replica behavior, MedicalRecordService infrastructure, database configuration, Azure deployment, and CI E2E orchestration.
- *Decisions:* Kept the changes separated by Jira story and commit purpose, verified each deployment and configuration area independently, and used the merged pull requests as the final traceable Sprint 2 DevOps evidence.

**QA -**
- *Progress:* Closed out the sprint's QA deliverables. Delivered SWC-86 (Selenium suite to 39 of 39 passing), SWC-87 (local JMeter Load and Stress for the queue-polling endpoints), and SWC-88 (Azure JMeter extension, all six thresholds passing). Reviewed and approved SWC-84 (5/5) and SWC-90 (4/4), and signed off SWC-91 as approved with conditions. Captured Jira evidence screenshots for SWC-81 and SWC-82.
- *Challenges:* SWC-84 changes deployment configuration only, so the live Azure deployment was the only place its criteria could be exercised. SWC-91 arrived with no QA test run requested or performed. The four Azure consultation errors in the SWC-88 Load run had to be traced before they could be reported.
- *Decisions:* Verified SWC-84's five acceptance criteria against the live Azure deployment rather than by reading the workflow file. Signed SWC-91 off as approved with conditions, naming the two verifications still required rather than recording untested criteria as passes. Traced all four SWC-88 errors to a test-data ordering artifact between the Smoke and Load runs, not an application fault, and added a row-skip step so it cannot repeat.

---

*End of Sprint 2 consolidated log.*
