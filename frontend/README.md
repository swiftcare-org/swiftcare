# SwiftCare Frontend

The React app used by clinic staff (Doctors, Receptionists, Admins). It talks only to the API Gateway — never directly to a backend service.

## What it does

- `LoginPage` — username/password sign-in with client-side blank/whitespace validation, server-error mapping (401/403/other), and role-based redirect after a successful login.
- `AuthContext` / `useAuth` — holds the decoded user in memory; the raw JWT lives in `sessionStorage` only (cleared when the tab closes — deliberate, for shared clinic workstations).
- `ProtectedRoute` — redirects unauthenticated visitors to `/login` and role-mismatched visitors to their own dashboard.
- `UserManagementPage` (Admin) — create-staff-account form (username, password, full name, role, room number for Doctors) with server-error mapping, plus a table of existing accounts.
- `PatientRegistrationPage` (Receptionist) — patient intake form (NIC, full name, date of birth, gender, address, phone number, blood group) mirroring PatientService's validation rules client-side.
- `PatientSearchPage` (Doctor, Receptionist, Admin) — debounced (300ms) live search by name, NIC, or phone number, suppressed below 2 characters. Distinct idle/searching/results/empty/error states; the receptionist empty state links to patient registration.
- `PatientProfilePage` (Doctor, Receptionist, Admin) — full patient details, allergies, and chronic conditions. Doctors see one red banner per allergy, one amber banner per chronic condition, and a blue banner when the latest completed consultation has an overdue follow-up, in that order. Receptionists can update permitted profile fields, check in returning patients, and manage chronic conditions.
- `QueueManagementPage` (Receptionist) — displays the current clinic day's full queue and polls every five seconds. It resolves names from PatientService by `PatientId`, caches successful lookups across polls, and shows a disabled `View Prescription` action for completed entries until SWC-30 implements the receptionist workflow.
- `DoctorDashboard` (Doctor) — displays the shared `WAITING` pool in queue-number order and polls every five seconds. It also loads and polls the doctor's current `IN_CONSULTATION` assignment from QueueService, so the card recovers after login in a fresh browser context and clears when the backend no longer reports an active assignment. A server-backed `Continue Prescription` action restores the latest completed consultation when it does not yet have a prescription. Doctors can call the first waiting patient only after their current assignment has loaded and no patient is assigned. Patient names are resolved through PatientService; waiting-pool lookups are cached between polls.
- `ConsultationPage` (Doctor) — loads the current queue assignment and any saved consultation, including after login in a fresh browser context. It loads templates from MedicalRecordService, pre-fills editable symptoms, examination findings, and notes, and accepts an optional follow-up date with instructions. After saving the consultation and vital signs, the doctor can complete it. The button stays disabled until vital signs are saved. A publish failure displays a retryable error; retry uses the stored event ID on the server.
- `PrescriptionPage` (Doctor) — opens after consultation completion succeeds, shows advisory allergy warnings and previous prescriptions, accepts multiple medicine items, and saves a `PENDING` prescription linked to the consultation, queue, patient, and authenticated doctor. If redirect state is unavailable after a refresh or new browser context, it recovers the latest unfinished prescription context from backend data.
- `WaitingRoomDisplayPage` (public) — available at `/queue/display` without login. Shows only current room-to-queue assignments and the next three waiting queue numbers, polls every five seconds, and uses a responsive layout suitable for a TV or monitor.

## Port

`5173` (Vite default, set explicitly in `vite.config.ts`).

## Required environment variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `VITE_GATEWAY_URL` | Base URL of the API Gateway | Required — the app throws at startup if unset. Local default: `http://localhost:8000` |

Copy `.env.example` to `.env` and adjust if needed:

```bash
cp .env.example .env
```

`.env` is never committed; only `.env.example` (placeholder values) is.

## Running locally

```bash
cd frontend
npm install
npm run dev
```

Requires the API Gateway (port 8000) and AuthService (port 5000) for login. PrescriptionService (port 5001) creates prescriptions and supplies patient prescription history; PatientService (port 5002) supplies patient details, allergies, and chronic conditions; QueueService (port 5003) supplies check-in, queue, and public waiting-room display data; and MedicalRecordService (port 5004) supplies consultation templates, consultation creation, vital-sign recording, completion, and overdue follow-ups. The frontend itself starts without them, but the corresponding requests fail. The public `/queue/display` page does not require AuthService or a user session.

The active patient assignment is not stored in browser `sessionStorage`; only authentication still uses that storage. The doctor dashboard calls `GET /api/queue/today/current` when it opens and every five seconds. A `204 No Content` response removes the current-patient card, while a failed lookup leaves Call Next disabled until the current state can be checked again. The consultation page loads the same assignment and the doctor's saved consultation when opened. After a successful completion event is processed by QueueService, the entry changes to `COMPLETED` and the current-patient card clears on the next poll.

## Build

```bash
npm run build
```

Type-checks (`tsc -b`) and produces a production bundle via Vite.

## Lint

```bash
npm run lint
```

Runs `oxlint`.

## Testing

No automated frontend test suite (Vitest/React Testing Library) exists yet. Frontend changes are checked with `npm run lint`, `npm run build`, and manual verification against the running application.

## Stack

React 19, TypeScript, Vite 8, Tailwind CSS v4, React Router 7.
