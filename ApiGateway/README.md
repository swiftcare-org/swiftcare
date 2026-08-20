# API Gateway

The single entry point for the React frontend. Every client request goes through the Gateway — never directly to a backend service. It handles CORS, correlation IDs, and the trust boundary between the client and internal services, then routes requests via YARP.

## What it does

- Reverse-proxies requests to backend services via [YARP](https://microsoft.github.io/reverse-proxy/).
- `CorrelationIdMiddleware` — generates `X-Correlation-ID` when a client omits it, sets it on both the request and response, and logs every incoming request under a correlation-scoped logger.
- `GatewayForwardingMiddleware` — strips any client-supplied `X-Gateway-Secret`, `X-User-Id`, `X-User-Role`, `X-User-Name`, or `X-Room-Number` header (a client must never be able to forge identity), then attaches the trusted `X-Gateway-Secret` before proxying.
- CORS restricted to the frontend's dev origin (`http://localhost:5173`).
- `GET /health` — liveness/readiness check.

## Port

`8000` (see `Properties/launchSettings.json`).

## Reverse proxy routes

Configured in `appsettings.json` under `ReverseProxy`:

| Route | Match | Destination |
| --- | --- | --- |
| `auth-route` | `/api/auth/{**catch-all}` | `http://localhost:5000` (AuthService) |

As more services come online (PatientService, QueueService, etc.), add a route + cluster entry per service rather than a shared routing abstraction — each route maps one URL prefix to one service's base address.

## Required environment variables

The Gateway fails fast at startup if this is missing — it will not silently start unable to authenticate its own outbound calls.

| Variable | Purpose | Notes |
| --- | --- | --- |
| `Gateway__InternalSecret` | Shared secret attached to every proxied request | Required, must match the value configured on every backend service (currently AuthService's `Gateway__InternalSecret`) |

## Running locally

```bash
cd ApiGateway

export Gateway__InternalSecret="<shared value, must match every backend service>"
export ASPNETCORE_ENVIRONMENT=Development

dotnet run
```

Start the backend services it proxies to (currently AuthService on port 5000) before exercising routed endpoints — `GET /health` on the Gateway itself works standalone.

## Testing

No automated test project exists for the Gateway yet (SWC-6 covers routing/middleware configuration only; AuthService carries the story's test coverage). Verify manually:

```bash
curl http://localhost:8000/health
curl -X POST http://localhost:8000/api/auth/login -H "Content-Type: application/json" -d "{\"username\":\"dr.chen\",\"password\":\"<seeded password>\"}"
```

## Endpoints

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/health` | Health check |
| `*` | `/api/auth/{**catch-all}` | Proxied to AuthService |
