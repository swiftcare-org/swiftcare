# SwiftCare — Sprint 1 Daily Stand-up Log
**Consolidated from:** Developer (IT24103439), DevOps (IT24103437), QA (IT24103431)
**Period:** 19th August - 1st September 2026

---

## Sprint 1 Story Summary

| Story | Title | Status | Dev Completed | QA Approved |
|---|---|---|---|---|
| SWC-6 | User Login | Merged | 20 Aug | 21 Aug |
| SWC-7 | User Logout | Merged | 22 Aug | 22 Aug |
| SWC-60 | E2E Suite: Login/Logout | Merged | — | 23 Aug |
| SWC-68 | Establish CI Pipeline and Repository Quality Gates | Merged | — | 23 Aug |
| SWC-69 | Implement Azure Deployment for API Gateway, AuthService, and Frontend | Merged | — | 23 Aug |
| SWC-8 | Create User Account | Merged | 25 Aug | 25 Aug |
| SWC-9 | Register New Patient | Merged | 26 Aug | 26 Aug |
| SWC-12 | Search Patient | Merged | 27 Aug | 27 Aug |
| SWC-66 | Extended E2E Suite | Merged | — | 27 Aug |
| SWC-70 | Integrate PatientService into CI/CD and Azure | Merged | — | 27 Aug |
| SWC-72 | Manage Azure Infrastructure with Terraform | Merged | — | 27 Aug |
| SWC-17 | Manage Allergies | Merged | 28 Aug | 28 Aug |
| SWC-19 | Auto Create Queue Entry | Merged | 29 Aug | 29 Aug |
| SWC-67 | Load and stress testing of sprint 1 API with JMeter | Merged | — | 30 Aug |
| SWC-71 | Deploy QueueService Through CI/CD | Merged | — | 29 Aug |
| Bug #22 | Admin could access patient registration | Fixed | 26 Aug | 26 Aug |

**Infrastructure/DevOps milestones:** repo & branch protection (19 Aug), CI pipelines (21–22 Aug), containerization & Azure CD (23 Aug), E2E stabilization (24 Aug), PatientService integration (27 Aug), QueueService integration (29 Aug).

**Performance testing:** JMeter Smoke/Load/Stress executed 30 Aug — Load passed all 6 criteria; Stress broke at ~200 concurrent users / ~355 req/s, root cause traced to MySQL CPU saturation (after a Kafka misconfiguration was ruled out and fixed).

---

## 19 August 2026

**DevOps —**
- *Progress:* Initialized the repository, project structure, Docker Compose infrastructure, environment template, and initial CI validation. Defined the API Gateway's role and configured branch rules for `main` and `develop`. Added "Validate repository infrastructure" as the first required status check on both branches.
- *Challenges:* Application projects were not yet available, so application builds and tests could not be configured fully.
- *Decisions:* Protected both branches from deletion/force pushes; required PRs, resolved conversations, passing checks, and an extra approval for unattributed Copilot PRs. Required two approvals on `main` (preferably QA + DevOps) with stale-approval dismissal, and one approval on `develop` (preferably QA).

**Developer —**
- *Progress:* No story work; waited for DevOps to establish the repo, folder structure, and branch rules.
- *Challenges:* No project skeleton existed yet, so there was nowhere to place code.
- *Decisions:* Used the day to read the README, product scope, and Sprint 1 backlog so coding could start immediately once the structure was ready, rather than build a throwaway structure.

---

## 20 August 2026

**Developer —**
- *Progress:* Completed **SWC-6 (User Login)**. Built the solution and AuthService, including `User` and `AuditLog` tables, the first migration, and a test-user seeder. Added BCrypt password checks and JWT issuance. Configured the API Gateway for routing, CORS, and forwarding to AuthService. Built the frontend login page, login-state handling, role-based routing, and the Admin/Doctor/Receptionist dashboards. 24/24 unit tests passing.
- *Challenges:* First working path spanning frontend to database, so every naming convention, folder layout, and pattern had to be established from scratch.
- *Decisions:* Only the Gateway validates the JWT; downstream services trust a shared internal secret. Roles are a fixed enum, not free text. All secrets come from environment variables. Failed logins return a generic message, and audit logs record only the user ID.

---

## 21 August 2026

**DevOps —**
- *Progress:* Started **SWC-68 (Establish CI Pipeline and Repository Quality Gates)** by adding CI jobs for .NET restore/build/test and frontend install/lint/build.
- *Challenges:* CI needed to work on branches where some application projects didn't exist yet.
- *Decisions:* Guarded jobs with project-file checks and documented framework versions. Added "Build and lint frontend" and "Build and test .NET projects" as required checks on both branches.

**Developer —**
- *Progress:* Started **SWC-7 (User Logout)**. Added the logout endpoint and audit log record in AuthService. Added JWT checking and a revoked-token list in the Gateway so tokens stop working immediately on logout.
- *Challenges:* Needed to block old tokens without breaking the Gateway's stateless design; hit a bug where revocation settings were read before configuration finished loading.
- *Decisions:* Block tokens at the Gateway by token ID rather than shortening expiry. Treat logout as best-effort on the frontend so the user is always signed out locally even if the backend call fails.

**QA —**
- *Progress:* Reviewed and approved **SWC-6 (User Login)**. 24/24 unit tests, 12/12 Postman cases; SQL injection and Gateway trust-boundary checks verified.
- *Challenges:* No protected endpoint yet existed to test Gateway token enforcement against.
- *Decisions:* Approved for merge. Deferred token-enforcement and E2E login coverage until SWC-7/SWC-8 landed.

---

## 22 August 2026

**DevOps —**
- *Progress:* Continued **SWC-68** by adding coverage reporting, a coverage gate, dependency scanning, migration validation, CodeQL analysis, formatting checks, and test summaries.
- *Challenges:* Generated migrations dragged down reported coverage; vulnerability-scan commands didn't always fail automatically on real issues.
- *Decisions:* Excluded generated migrations from coverage and set a 55% threshold; began explicitly inspecting dependency-audit output. Added "Scan dependencies for vulnerabilities" and "Validate EF Core migrations" as required checks on both branches, plus "Analyze code (csharp)" and "Analyze code (javascript-typescript)" as additional required checks on `main`.

**Developer —**
- *Progress:* Completed **SWC-7**. Connected the frontend sign-out button to the logout endpoint, fixed the configuration bug and review-flagged error-response gaps, and finished testing (32/32 AuthService, 25/25 Gateway). QA approved and the story was merged.
- *Challenges:* Had to prove a user couldn't forge identity headers to impersonate someone else.
- *Decisions:* Gateway strips any client-sent identity headers before forwarding requests. Standardized error format across all auth endpoints.

**QA —**
- *Progress:* Reviewed and approved **SWC-7 (User Logout)**. 32/32 + 25/25 unit tests, 6/6 Postman cases; audit log and forged-header handling verified.
- *Challenges:* None.
- *Decisions:* Approved for merge; no open bugs.

---

## 23 August 2026

**DevOps —**
- *Progress:* Completed **SWC-68** with deployable-image validation and completed **SWC-69 (Implement Azure Deployment for API Gateway, AuthService, and Frontend)** by containerizing the Gateway and AuthService, expanding the local Docker stack, and implementing the initial Azure CD workflow for backend and frontend.
- *Challenges:* Deployment required finalized migration and admin-bootstrap processes plus external Azure/GitHub configuration.
- *Decisions:* Adopted immutable commit-SHA images, Azure OIDC authentication, GitHub Environment secrets, internal-only AuthService ingress, and public Gateway ingress. Added "Build deployable images" as a required check on `main`.

**Developer —**
- *Progress:* No story work; waited for the AuthService deployment work (Docker image, Azure pipeline, migrate/bootstrap-admin commands) to finish before starting the next story.
- *Challenges:* Starting SWC-8 while those same files were still in flux would have caused merge conflicts.
- *Decisions:* Waited instead of branching early, and reviewed the deployment handover notes so SWC-8 could reuse existing password rules and admin-bootstrap logic.

**QA —**
- *Progress:* Reviewed and approved **SWC-60**, the first Selenium E2E suite (login/logout). 8/8 tests passing against the real stack.
- *Challenges:* The E2E project wasn't yet wired into CI; needed a dedicated job (Docker + frontend + Chrome).
- *Decisions:* Approved and merged; tracked CI wiring as a separate, non-blocking follow-up.

---

## 24 August 2026

**DevOps —**
- *Progress:* Fixed an intermittent MySQL startup failure affecting Selenium E2E tests after merges to `develop`.
- *Challenges:* The original health check reported MySQL healthy before TCP connections were actually ready.
- *Decisions:* Switched to a TCP health check and added retries for database migrations on transient startup failures. Added "Run E2E tests" as a required check on `main` once the E2E environment stabilized.

**Developer —**
- *Progress:* Started **SWC-8 (Create User Account)**. Added the user validation/creation service, `POST /api/users` and `GET /api/users` endpoints, an Admin-only Gateway rule/route, the frontend API client with field-level errors, and the admin user-management page.
- *Challenges:* A security scan flagged that user-typed text could be written directly into logs and used to forge log lines.
- *Decisions:* The service trusts the Gateway's headers rather than re-validating the JWT itself. Reused the existing minimum-password-length setting instead of hardcoding it. Sanitized user-supplied text before logging and documented the one false-positive scanner warning.

---

## 25 August 2026

**Developer —**
- *Progress:* Completed **SWC-8**. Verified hashed password storage, Admin-only user creation, duplicate-username rejection, and reuse of deleted usernames. QA approved with 14/14 test cases; story merged.
- *Challenges:* QA found `fullName` was a required field that had never been documented.
- *Decisions:* Kept the field required and fixed the documentation rather than loosening the rule. Confirmed deleted-account usernames can be reused so cleanup never permanently locks a name.

**QA —**
- *Progress:* Reviewed and approved **SWC-8 (Create User Account)**. 14/14 Postman cases; BCrypt storage and soft-deleted-username reuse verified.
- *Challenges:* Found an undocumented required field (`fullName`) in the DTO during setup.
- *Decisions:* Corrected test payloads and proceeded — ruled not a defect. Closed the deferred SWC-6 Gateway-coverage item as redundant. Approved for merge.

---

## 26 August 2026

**Developer —**
- *Progress:* Completed **SWC-9 (Register New Patient)**. Built PatientService from scratch, including the `Patient` table and first migration. Added the project's first Kafka publisher, emitting a `patient-checked-in` event. Added the registration endpoint with Sri Lankan NIC/phone validation, the Gateway route/access rule, and the frontend registration form, plus a helper to sanitize user input before logging.
- *Challenges:* The story called for showing a queue number on success, but QueueService (which owns queue numbers) didn't exist yet. QA also raised **Bug #22**: Admins could access the registration page.
- *Decisions:* Left the queue number out and documented it as a known gap in the PR rather than generating it from the wrong service. Restricted registration to Receptionists in three places (Gateway rule, service, frontend route). Never log the NIC, since it's personal data. If the patient record saves but the Kafka publish fails, still return success and log the failure — a full retry mechanism deferred to a future story.

**QA —**
- *Progress:* Logged **Bug #22** (Admin patient-registration scope conflict). Reviewed and approved **SWC-9**. 13/13 Postman cases; Kafka PHI check and Kafka-outage resilience verified.
- *Challenges:* Determining whether Admin registration access was a bug or an intentional decision.
- *Decisions:* Logged as a High-priority bug with an agreed Receptionist-only fix, and verified the fix live before approving SWC-9.

---

## 27 August 2026

**DevOps —**
- *Progress:* Completed **SWC-70 (Integrate PatientService into CI/CD and Azure)** and **SWC-72 (Manage Azure Infrastructure with Terraform)**. Integrated PatientService into Docker, CI, CD, Azure, and Gateway routing, extended E2E CI for PatientService and Kafka, and adopted the existing Azure infrastructure into Terraform remote state.
- *Challenges:* Terraform imports and Azure operations were interrupted by network resets, requiring state reconciliation.
- *Decisions:* Gave PatientService an isolated database account, standardized on Terraform for stable infrastructure, and kept application deployments under CD ownership.

**Developer —**
- *Progress:* Completed **SWC-12 (Search Patient)** — search endpoint, Gateway route, dedicated search page, and unit tests; QA approved 13/13 cases. Started **SWC-17 (Manage Allergies)**: added the `Allergy` table/migration and began service logic.
- *Challenges:* Deciding where allergies should live, since the README assigns clinical records to MedicalRecordService, which is still an empty placeholder.
- *Decisions:* Search returns an empty list (not an error) for empty or sub-two-character queries, avoiding a validation error while the user is still typing. Case-insensitive matching is built into the query itself, since the in-memory test database is case-sensitive. Kept allergies in PatientService for now and documented the reasoning so the future move isn't a surprise.

**QA —**
- *Progress:* Reviewed and approved **SWC-12 (Search Patient)**. 13/13 Postman cases. Ran the extended E2E suite (**SWC-66**): 14 new tests plus 2 smoke journeys across SWC-8/9/12/17.
- *Challenges:* A smoke-journey navigation test failed because CSS `text-transform: uppercase` broke `By.LinkText` matching.
- *Decisions:* Curated E2E scope to happy paths and UI-only behavior. Fixed the locator to match by `href` instead. Deferred SWC-9 queue-number assertions until SWC-19 landed.

---

## 28 August 2026

**Developer —**
- *Progress:* Completed **SWC-17 (Manage Allergies)**. Added allergy and patient-profile logic, endpoints for viewing a patient and managing allergies, Gateway routes/access rules, and the patient profile page with allergy list and red warning banner. Updated service documentation. QA approved 18/18 test cases.
- *Challenges:* Sorting allergies by severity in the database produced alphabetical (wrong) order. The search route also had to be matched before the "view patient by ID" route, or every search would be treated as an ID.
- *Decisions:* Sorted severity in code instead (Severe → Moderate → Mild, newest first). Switched to the real `Guid` ID type instead of the number type specified in the story, which would have failed for real patients. Removed allergies are soft-deleted, not erased, to preserve the medical audit trail. Doctors and Receptionists can add/remove allergies; Admins can only view. Also opened patient search to Doctors and Admins, since doctors otherwise had no way to reach a patient profile.

**QA —**
- *Progress:* Reviewed and approved **SWC-17 (Manage Allergies)**. 18/18 Postman cases; cross-patient isolation and role-based CRUD verified.
- *Challenges:* None significant.
- *Decisions:* Approved for merge after independently confirming isolation and write-rejection at the live API.

---

## 29 August 2026

**DevOps —**
- *Progress:* Completed **SWC-71 (Deploy QueueService Through CI/CD)** by integrating QueueService into Docker, CI, CD, Terraform, and Azure; verified it consumed a `patient-checked-in` event and created a queue entry.
- *Challenges:* The first revision-readiness parser was incorrect, and Kafka briefly reported a missing topic after infrastructure recreation.
- *Decisions:* Deployed QueueService as a private background worker with no ingress, gave it an isolated database account, and retained Terraform-based messaging shutdown for cost control.

**Developer —**
- *Progress:* Completed **SWC-19 (Auto Create Queue Entry)**. Built QueueService as a standalone service with its own tables/migration, logic to assign the next daily queue number (Q-001, Q-002…), and a Kafka listener reacting to `patient-checked-in`. Registered patients now join the queue automatically. 24/24 unit tests passing; all four acceptance scenarios verified end to end.
- *Challenges:* Kafka can redeliver messages, risking duplicate queue entries. No "returning patient check-in" screen existed yet, so the same-patient-twice-in-one-day scenario couldn't be triggered through the app.
- *Decisions:* Only marked a Kafka message handled after the database change was safely saved, and tracked handled event IDs to ignore repeats. Added two unique database constraints as a final safety net. Used the clinic's local time zone (Asia/Colombo) so queue numbering resets at local, not UTC, midnight. Duplicated the event format in QueueService rather than linking to PatientService, to keep services independent. Tested the unreachable scenario by sending the event manually via the Kafka UI, noting the gap in the PR.

**QA —**
- *Progress:* Completed independent QA verification of **SWC-19**. 4/4 acceptance criteria plus 7 additional cases passed via Postman, Kafka UI, and MySQL.
- *Challenges:* Postman couldn't send a crafted CR/LF sequence for the log-injection test.
- *Decisions:* Verified sanitization via a raw TCP socket instead. Removed one planned test case as unnecessary. Issued QA approval with no defects.

---

## 30 August 2026

**QA —**
- *Progress:* Ran JMeter Smoke, Load, and Stress tests against the Sprint 1 API. Load testing passed all 6 criteria. Stress testing found the breaking point at approximately 200 concurrent users / ~355 requests per second.
- *Challenges:* Found a Kafka misconfiguration causing 5-second registration delays; the initial bottleneck hypothesis was incorrect.
- *Decisions:* Fixed the Kafka configuration and logged the defect. Re-ran Stress testing with direct monitoring and confirmed MySQL CPU as the actual bottleneck.

---

*End of Sprint 1 consolidated log.*
