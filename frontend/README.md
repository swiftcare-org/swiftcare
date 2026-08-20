# SwiftCare Frontend

The React app used by clinic staff (Doctors, Receptionists, Admins). It talks only to the API Gateway — never directly to a backend service.

## What it does

- `LoginPage` — username/password sign-in with client-side blank/whitespace validation, server-error mapping (401/403/other), and role-based redirect after a successful login.
- `AuthContext` / `useAuth` — holds the decoded user in memory; the raw JWT lives in `sessionStorage` only (cleared when the tab closes — deliberate, for shared clinic workstations).
- `ProtectedRoute` — redirects unauthenticated visitors to `/login` and role-mismatched visitors to their own dashboard.
- Placeholder `DoctorDashboard` / `ReceptionistDashboard` / `AdminDashboard` (real content lands in later stories).

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

Requires the API Gateway (port 8000) and AuthService (port 5000) running for the login flow to actually authenticate — the frontend itself will start and render without them, but sign-in requests will fail.

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

No automated frontend test suite (Vitest/React Testing Library) exists yet — it is explicitly deferred to a QA-owned story, not part of SWC-6's developer scope. Client-side validation (empty/whitespace fields) was verified manually against the running dev server.

## Stack

React 19, TypeScript, Vite 8, Tailwind CSS v4, React Router 7.
