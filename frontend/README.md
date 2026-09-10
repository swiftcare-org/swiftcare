# SwiftCare Frontend

The React app used by clinic staff (Doctors, Receptionists, Admins). It talks only to the API Gateway — never directly to a backend service.

## What it does

- `LoginPage` — username/password sign-in with client-side blank/whitespace validation, server-error mapping (401/403/other), and role-based redirect after a successful login.
- `AuthContext` / `useAuth` — holds the decoded user in memory; the raw JWT lives in `sessionStorage` only (cleared when the tab closes — deliberate, for shared clinic workstations).
- `ProtectedRoute` — redirects unauthenticated visitors to `/login` and role-mismatched visitors to their own dashboard.
- `UserManagementPage` (Admin) — create-staff-account form (username, password, full name, role, room number for Doctors) with server-error mapping, plus a table of existing accounts.
- `PatientRegistrationPage` (Receptionist) — patient intake form (NIC, full name, date of birth, gender, address, phone number, blood group) mirroring PatientService's validation rules client-side.
- `PatientSearchPage` (Doctor, Receptionist, Admin) — debounced (300ms) live search by name, NIC, or phone number, suppressed below 2 characters. Distinct idle/searching/results/empty/error states; the receptionist empty state links to patient registration.
- `PatientProfilePage` (Doctor, Receptionist, Admin) — full patient details, allergies, and chronic conditions. Receptionists can update permitted profile fields, check in returning patients, and manage chronic conditions.
- `QueueManagementPage` (Receptionist) — displays the current clinic day's full queue and polls every five seconds. It resolves names from PatientService by `PatientId`, caches successful lookups across polls, and leaves prescription status neutral until SWC-30 provides PrescriptionService.
- `DoctorDashboard` (Doctor) — displays the shared `WAITING` pool in queue-number order and polls every five seconds. Doctors can call the first waiting patient, see the current patient assignment, and are prevented from calling another while occupied. Patient names are resolved and cached through PatientService.

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

Requires the API Gateway (port 8000) and AuthService (port 5000) for login. PatientService (port 5002) supplies patient details, while QueueService (port 5003) supplies check-in and queue data. The frontend itself starts without them, but the corresponding requests fail.

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
