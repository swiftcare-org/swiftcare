# API Gateway

The single entry point for the React frontend. Every client request goes through the Gateway — never directly to a backend service. It handles CORS, correlation IDs, and the trust boundary between the client and internal services, then routes requests via YARP.

## What it does

- Reverse-proxies requests to backend services via [YARP](https://microsoft.github.io/reverse-proxy/).
- `CorrelationIdMiddleware` — generates `X-Correlation-ID` when a client omits it, sets it on both the request and response, and logs every incoming request under a correlation-scoped logger.
- JWT bearer authentication — validates tokens issued by AuthService (signature, issuer, audience, lifetime) against the `Jwt:*` configuration below. Routes are protected per-route via `appsettings.json`'s `AuthorizationPolicy` (`Anonymous` or `Default`), not globally.
- `TokenRevocationMiddleware` — maintains an in-memory revoked-`jti` denylist. Revokes the caller's token on `POST /api/auth/logout` *before* forwarding, so logout always terminates the session locally even if AuthService is unreachable. Rejects any request bearing an already-revoked token with `401`. In-memory and per-instance — does not survive a Gateway restart and is not shared across multiple Gateway instances; acceptable for a single-instance Sprint 1 deployment only.
- `GatewayForwardingMiddleware` — strips any client-supplied `X-Gateway-Secret`, `X-User-Id`, `X-User-Role`, `X-User-Name`, or `X-Room-Number` header (a client must never be able to forge identity), then attaches the trusted `X-Gateway-Secret` and, for authenticated requests, identity headers derived from the validated JWT's claims.
- CORS restricted to the frontend's dev origin (`http://localhost:5173`).
- `GET /health` — liveness/readiness check, open to anonymous requests.

## Port

`8000` (see `Properties/launchSettings.json`).

## Reverse proxy routes

Configured in `appsettings.json` under `ReverseProxy`:

| Route | Match | `AuthorizationPolicy` | Destination |
| --- | --- | --- | --- |
| `auth-login-route` | `POST /api/auth/login` | `Anonymous` | `http://localhost:5000` (AuthService) |
| `auth-route` | `/api/auth/{**catch-all}` | `Default` (requires a valid, non-revoked JWT) | `http://localhost:5000` (AuthService) |
| `users-route` | `GET, POST /api/users` | `AdminOnly` | `http://localhost:5000` (AuthService) |
| `patients-route` | `POST /api/patients` | `ReceptionistOnly` | `http://localhost:5002` (PatientService) |
| `patients-search-route` | `GET /api/patients/search` | `PatientSearchAndReadPolicy` | `http://localhost:5002` (PatientService) |
| `patient-allergies-read-route` | `GET /api/patients/{id:guid}/allergies` | `PatientSearchAndReadPolicy` | `http://localhost:5002` (PatientService) |
| `patient-allergies-write-route` | `POST /api/patients/{id:guid}/allergies` | `AllergyWritePolicy` | `http://localhost:5002` (PatientService) |
| `patient-allergy-item-route` | `PUT, DELETE /api/patients/{id:guid}/allergies/{aid:guid}` | `AllergyWritePolicy` | `http://localhost:5002` (PatientService) |
| `patient-read-route` | `GET /api/patients/{id:guid}` (`Order: 2`) | `PatientSearchAndReadPolicy` | `http://localhost:5002` (PatientService) |

`patient-read-route` is deliberately constrained to `{id:guid}` and ordered after `patients-search-route`: this guarantees `/api/patients/search` can never be shadowed by the parameterized route regardless of Order, since `"search"` fails the guid constraint outright. `PatientSearchAndReadPolicy` (Doctor, Receptionist, Admin) and `AllergyWritePolicy` (Doctor, Receptionist — Admin is read-only for allergies by stakeholder decision) are defined alongside `AdminOnly` and `ReceptionistOnly` in `Program.cs`.

As more services come online (PatientService, QueueService, etc.), add a route + cluster entry per service rather than a shared routing abstraction — each route maps one URL prefix to one service's base address. Give each new protected route an explicit `AuthorizationPolicy` rather than relying on an implicit default.

## Required environment variables

The Gateway fails fast at startup if any of these are missing — it will not silently start unable to authenticate its own outbound calls or validate inbound tokens.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `Gateway__InternalSecret` | Shared secret attached to every proxied request | Required, must match the value configured on every backend service (currently AuthService's `Gateway__InternalSecret`) |
| `Jwt__SecretKey` | HMAC-SHA256 key used to validate AuthService-issued tokens | Required, must be at least 32 bytes and **must exactly match** AuthService's `Jwt__SecretKey`. A mismatch fails closed — every authenticated route returns 401 — rather than open. |
| `Jwt__Issuer` | Expected token issuer | Required, must exactly match AuthService's `Jwt__Issuer` |
| `Jwt__Audience` | Expected token audience | Required, must exactly match AuthService's `Jwt__Audience` |

## Running locally

```bash
cd ApiGateway

export Gateway__InternalSecret="<shared value, must match every backend service>"
export Jwt__SecretKey="<shared value, must match AuthService's Jwt__SecretKey>"
export Jwt__Issuer="<shared value, must match AuthService's Jwt__Issuer>"
export Jwt__Audience="<shared value, must match AuthService's Jwt__Audience>"
export ASPNETCORE_ENVIRONMENT=Development

dotnet run
```

Start the backend services it proxies to (currently AuthService on port 5000) before exercising routed endpoints — `GET /health` on the Gateway itself works standalone.

## Testing

`tests/ApiGateway.UnitTests/`:

- `Middleware/` — pure unit tests over `TokenRevocationMiddleware` and `GatewayForwardingMiddleware` in isolation (no HTTP pipeline).
- `Security/` — `RevokedTokenStore` lookup/eviction behavior.
- `Routing/` — boots the real Gateway pipeline (`WebApplicationFactory<Program>`) against the actual `appsettings.json` route configuration, so a wrong `AuthorizationPolicy` value or route `Order` is caught by a test rather than only by manual verification.

```bash
dotnet test tests/ApiGateway.UnitTests
```

Manual verification:

```bash
curl http://localhost:8000/health
curl -X POST http://localhost:8000/api/auth/login -H "Content-Type: application/json" -d "{\"username\":\"dr.chen\",\"password\":\"<seeded password>\"}"
```

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `GET` | `/health` | none | Health check |
| `POST` | `/api/auth/login` | none | Proxied to AuthService |
| `POST` | `/api/auth/logout` | Bearer JWT | Revokes the token's `jti`, then proxies to AuthService |
| `*` | `/api/auth/{**catch-all}` | Bearer JWT | Proxied to AuthService |
| `GET`, `POST` | `/api/users` | Bearer JWT, `Admin` role | Proxied to AuthService |
| `POST` | `/api/patients` | Bearer JWT, `Receptionist` role | Proxied to PatientService |
| `GET` | `/api/patients/search` | Bearer JWT, `Doctor`\|`Receptionist`\|`Admin` role | Proxied to PatientService |
| `GET` | `/api/patients/{id}` | Bearer JWT, `Doctor`\|`Receptionist`\|`Admin` role | Proxied to PatientService |
| `GET` | `/api/patients/{id}/allergies` | Bearer JWT, `Doctor`\|`Receptionist`\|`Admin` role | Proxied to PatientService |
| `POST` | `/api/patients/{id}/allergies` | Bearer JWT, `Doctor`\|`Receptionist` role | Proxied to PatientService |
| `PUT`, `DELETE` | `/api/patients/{id}/allergies/{allergyId}` | Bearer JWT, `Doctor`\|`Receptionist` role | Proxied to PatientService |
