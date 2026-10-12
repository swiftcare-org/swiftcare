# AuthService

## Durable sessions (SWC-157)

Apply AddDurableSessions before deploying AuthService, then update every gateway replica. Existing accounts receive a unique session version. Newly issued JWTs include that version; legacy tokens without it require a new login.

The gateway validates each authenticated request against the current active account, session version and durable token revocation. Deactivation, reactivation, password reset, role changes, room changes and soft deletion rotate the account version. Reactivation never restores an old session. Logout persists the token ID and expiry before forwarding the audit request; other tokens for the same unchanged account remain valid.

Expired revocations are removed in indexed batches of at most 500 on subsequent validation requests. Tokens and secrets are never logged. The internal session endpoint requires the gateway secret and has no public proxy route. An unavailable authority fails closed with 503; the frontend retains the session and offers a retry until logout is confirmed or the token is already rejected.

Retain RevokedSessions and account session versions during rollback. Returning a gateway to a version without durable checks would restore the original security gap. Deploy the AuthService endpoint before updating the gateways. Tests use SWIFTCARE_TEST_MYSQL only for isolated temporary databases.

Issues and validates the credentials for SwiftCare staff accounts. AuthService owns the `swiftcare_auth` database exclusively — no other service may query or write to it.

## What it does

- `POST /api/auth/login` — validates a username/password pair and issues a JWT (12-hour expiry) carrying `sub` (userId), `fullName`, `role`, and `roomNumber` (Doctor accounts only).
- `POST /api/auth/logout` — records a `LogoutAuditEntry` (userId, correlation ID, IP, timestamp) for the calling user. Always returns `204`, even if the token was never revoked at the Gateway; ending the session client-side never depends on this call succeeding.
- `POST /api/users` — creates a staff account (username, password, full name, role, room number for Doctors only). Admin-only. Rejects a duplicate username and an under-length password with a per-field validation error.
- `GET /api/users` — lists all staff accounts (no passwords). Admin-only.
- `GET /api/users/doctors`: lists active doctors (name, room, specialization) for every staff role. No account details are returned.
- `PUT /api/users/{id}`: edits the full name, and the room number and specialization for Doctors. Admin-only. The username and role cannot be changed.
- `PUT /api/users/{id}/reset-password`: stores a BCrypt hash of a new password, applying the same minimum length as account creation. Admin-only.
- `PUT /api/users/{id}/deactivate` and `PUT /api/users/{id}/activate`: toggle `IsActive`. Admin-only. An admin cannot deactivate their own account. A deactivated account is refused at the next sign-in; a token it already holds stays valid until it expires.
- `GET /api/audit-logs`: lists sign-in, sign-out and account management events, newest first (default 200, `?limit=` up to 500). Admin-only and read-only.
- Writes one `AdminAuditEntry` for every account created, edited, deactivated, reactivated or given a new password, in the same unit of work as the change itself.
- `GET /health` — liveness/readiness check.
- Writes one `LoginAuditEntry` per login attempt (`Success`, `InvalidCredentials`, or `AccountDeactivated`) and one `LogoutAuditEntry` per logout, without ever storing the attempted username or password.
- Enforces the Gateway trust boundary: every request except `/health` must carry a valid `X-Gateway-Secret` header.

## Port

`5000` (see `Properties/launchSettings.json`).

## Dependencies

- .NET 10 SDK
- MySQL 8.4 reachable at the connection string in `ConnectionStrings:AuthDb` (see the repository root `docker-compose.yml` for local MySQL)
- EF Core 9 / Pomelo MySQL provider (pinned below EF Core 10 until Pomelo releases `net10` support)
- `dotnet-ef` as a local tool (already restored via the repository's `dotnet-tools.json`)

## Required environment variables

AuthService fails fast at startup if any of these are missing or invalid — it will not silently start in a misconfigured state.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `ConnectionStrings__AuthDb` | MySQL connection string for `swiftcare_auth` | Required |
| `Jwt__SecretKey` | HS256 signing key for issued JWTs | Required, must be **≥ 32 bytes** |
| `Gateway__InternalSecret` | Shared secret validated on every non-health request | Required, must match the API Gateway's `Gateway__InternalSecret` |
| `AUTH_SEED_PASSWORD` | Password hashed for the four synthetic development accounts | Optional — only read when `ASPNETCORE_ENVIRONMENT=Development`; seeding is skipped (with a warning) if unset |

`Jwt:Issuer`, `Jwt:Audience`, and `Jwt:ExpiryHours` are non-secret and already set in `appsettings.json` (12-hour default expiry).

Never hardcode these values in source or commit them to `.env`. Set them via your shell, a local `.env` (never committed), or the orchestrator's secret store.

## Running locally

```bash
# from the repository root, with MySQL up (docker compose up -d)
cd services/AuthService

export ConnectionStrings__AuthDb="Server=localhost;Port=3306;Database=swiftcare_auth;User=<user>;Password=<password>;"
export Jwt__SecretKey="<32+ byte local development key>"
export Gateway__InternalSecret="<shared value, must match ApiGateway>"
export AUTH_SEED_PASSWORD="<password for the seeded dev accounts>"
export ASPNETCORE_ENVIRONMENT=Development

dotnet ef database update
dotnet run
```

## API documentation

With `ASPNETCORE_ENVIRONMENT=Development`, an interactive API explorer (Scalar) is served at [`http://localhost:5000/scalar/v1`](http://localhost:5000/scalar/v1), reading the raw OpenAPI document at `/openapi/v1.json`. Both are reachable without an `X-Gateway-Secret` header — `GatewaySecretMiddleware` exempts them the same way it exempts `/health` — but they do not exist at all outside Development, since `MapOpenApi()`/`MapScalarApiReference()` are only registered inside that environment guard.

On first run in Development, `DevelopmentSeeder` creates four synthetic accounts (one active Doctor with a room number, one active Receptionist, one active Admin, one deactivated Doctor) using `AUTH_SEED_PASSWORD` for all four. Never use real patient or staff data here — synthetic data only.

## Testing

```bash
dotnet test tests/AuthService.UnitTests/AuthService.UnitTests.csproj
```

Tests use EF Core InMemory and Moq exclusively — no real database or network connection is required to run them. Coverage includes credential verification (including the dummy-hash timing mitigation for unknown usernames), JWT claim structure, logout audit recording, user-account creation validation (duplicate username, password policy, room number required for Doctors), the full login/logout/users controller pipelines via `WebApplicationFactory`, and the gateway-secret middleware.

## Endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/auth/login` | `X-Gateway-Secret` | Validates credentials, returns a JWT + user summary on success |
| `POST` | `/api/auth/logout` | `X-Gateway-Secret`, `X-User-Id` | Records a logout audit entry, returns `204` |
| `POST` | `/api/users` | `X-Gateway-Secret`, `X-User-Role: Admin` | Creates a staff account, returns the created user summary |
| `GET` | `/api/users` | `X-Gateway-Secret`, `X-User-Role: Admin` | Lists all staff accounts |
| `GET` | `/api/users/doctors` | `X-Gateway-Secret`, any staff `X-User-Role` | Lists active doctors with room and specialization |
| `PUT` | `/api/users/{id}` | `X-Gateway-Secret`, `X-User-Role: Admin` | Edits full name, room number and specialization |
| `PUT` | `/api/users/{id}/reset-password` | `X-Gateway-Secret`, `X-User-Role: Admin` | Sets a new password |
| `PUT` | `/api/users/{id}/deactivate` | `X-Gateway-Secret`, `X-User-Role: Admin`, `X-User-Id` | Deactivates the account; `400` for the caller's own account |
| `PUT` | `/api/users/{id}/activate` | `X-Gateway-Secret`, `X-User-Role: Admin` | Reactivates the account |
| `GET` | `/api/audit-logs` | `X-Gateway-Secret`, `X-User-Role: Admin` | Lists audit entries, newest first |

User IDs are GUIDs. The all-zero GUID returns `400`, and an unknown or soft-deleted account returns `404`.

The IP address stored with each audit entry is the address AuthService sees for the connection. Behind the Gateway this is the Gateway's address, not the browser's, so the Audit Log page does not show it. It is still stored and returned by the API, ready for when the real client address is forwarded.
| `GET` | `/health` | none | Health check |

See `Controllers/AuthController.cs` and `Controllers/UsersController.cs` for the exact request/response contracts and status-code mapping.
