# E2ETests

Selenium end-to-end tests that drive the real frontend, Gateway and AuthService
through a Chrome browser — covering [SWC-6](https://swiftcare-app.atlassian.net/browse/SWC-6)
(User Login) and [SWC-7](https://swiftcare-app.atlassian.net/browse/SWC-7) (User Logout).
Tracked under [SWC-60](https://swiftcare-app.atlassian.net/browse/SWC-60).

Unlike `AuthService.UnitTests` / `ApiGateway.UnitTests`, this project has no
`ProjectReference` to any service — it only talks to whatever is already
running, over HTTP.

## Prerequisites

1. Backend stack, from the repo root:

   ```bash
   docker-compose up -d mysql authservice apigateway
   ```

2. Frontend dev server:

   ```bash
   cd frontend
   npm ci
   npm run dev
   ```

3. `AUTH_SEED_PASSWORD` set in the shell running the tests, matching the
   value used to seed the AuthService database (see repo root `.env`). This
   is the password for all four development-seeded accounts, e.g. `dr.chen`.

## Running

```bash
AUTH_SEED_PASSWORD=<value from .env> dotnet test tests/E2ETests
```

Environment variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `AUTH_SEED_PASSWORD` | *(required)* | Password for the seeded accounts |
| `E2E_BASE_URL` | `http://localhost:5173` | Frontend URL to drive |
| `E2E_HEADLESS` | `true` | Set to `false` to watch the browser locally |

`WebDriverManager` resolves and downloads a matching `chromedriver` for the
locally installed Chrome automatically — no manual driver setup needed.
