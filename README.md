# Transactions Dispute Portal

A web app where bank customers look through their card transactions, dispute the ones that don't look right, and follow each dispute through to the outcome. A small back office lets the disputes team pick up disputes and resolve them.

- **Front end:** a React 19 and TypeScript app (`src/DisputePortal.Client`) that does everything through the JSON API.
- **Back end:** .NET 10 with Domain-Driven Design and Clean Architecture, using only what the framework provides: ASP.NET Core, EF Core (SQLite), Identity and the built-in JWT bearer handler. The API is secured with short-lived JWT access tokens and rotating refresh tokens.

The back end serves the React app from the same address as the API.

## Run it with Docker

From the folder that contains the `Dockerfile`:

```bash
docker build -t dispute-portal .
```

```bash
docker run --rm -p 8080:8080 -v dispute-portal-data:/app/data -e DemoData__Enabled=true -e DemoData__Password='Demo#Passw0rd' -e Jwt__SigningKey='replace-me-with-a-long-random-secret-123456' dispute-portal
```

`Jwt__SigningKey` is required and must be at least 32 characters. The image doesn't contain one on purpose.

Open **http://localhost:8080** and sign in with a demo account (password `Demo#Passw0rd`):

| Who | Email | What you'll see |
|---|---|---|
| Amanda Mitchell (customer) | `amanda.mitchell@mmbank.co.za` | Transactions and a dispute history covering every outcome |
| Sipho Dlamini (customer) | `sipho.dlamini@mmbank.co.za` | Transactions, no disputes yet |
| Ayanda Nkosi (customer) | `ayanda.nkosi@mmbank.co.za` | One dispute just submitted |
| Naledi Khumalo (disputes team) | `naledi.khumalo@mmbank.co.za` | The dispute queue |

Try this: as Amanda, search for **Kauai**. There are two identical charges a minute apart, so dispute one. Then sign in as Naledi, start a review and uphold it. Back as Amanda, the history shows the outcome.

Demo data is off by default and is only seeded into an empty database.

## Run it without Docker

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Node.js 24](https://nodejs.org/). Build the React app into the back end, then start it:

```bash
cd src/DisputePortal.Client && npm ci && npm run build && cd ../..
```

```bash
dotnet run --project src/DisputePortal.Api
```

Open http://localhost:5260. In Development, demo data and a development-only JWT key are switched on for you.

To work on the React app, keep the back end running and start the app's dev server in a second terminal. It reloads as you edit and passes `/api` calls to the back end:

```bash
cd src/DisputePortal.Client && API_URL=http://localhost:5260 npm run dev
```

Then open http://localhost:5173.

## Run the tests

```bash
dotnet test
```

That runs the 84 back-end tests:

- **Unit tests** cover the domain rules, every use-case handler, and architecture checks that fail the build if the layering is broken.
- **Integration tests** cover persistence against a real SQLite database, including the outbox and its retries. They also boot the whole app over HTTP to test the API, sign-in, the refresh-token cookie, and how the React app is served.

The React app has its own tests:

```bash
cd src/DisputePortal.Client && npm test
```

- **22 component tests** (Vitest, Testing Library and MSW) render the real routes and screens with only the network faked. They cover:
  - sign-in, picking a session back up after a reload, and guarded routes
  - searching, form validation and server errors
  - withdrawing a dispute
  - the agent review rules
  - token refresh, including a check that parallel requests share a single refresh, since the server treats a reused refresh token as theft
- **One end-to-end test** (Playwright) drives a real browser against the running app: a customer raises a dispute, an agent upholds it, and the customer sees the outcome. Start the Docker container first, then:

```bash
cd src/DisputePortal.Client && npx playwright install chromium && npm run e2e
```

To run the back-end and component tests in Docker instead:

```bash
docker build --target test .
```

## Use the API

Get a token:

```bash
curl -s -X POST http://localhost:8080/api/auth/token -H "Content-Type: application/json" -d '{"email":"sipho.dlamini@mmbank.co.za","password":"Demo#Passw0rd"}'
```

Then send it on every call:

```bash
curl -s "http://localhost:8080/api/transactions?pageSize=5" -H "Authorization: Bearer <accessToken>"
```

Access tokens last 15 minutes. Use `/api/auth/refresh` to swap the refresh token for a new pair. Each refresh token works once, and reusing an old one revokes the session.

Browsers send `"useCookie": true` when signing in. The refresh token then comes back as a cookie instead of in the response body. The cookie is HttpOnly, Secure and SameSite=Strict, and is only sent to `/api/auth`. `refresh` and `revoke` read it when the request has no body.

| Method | Path | What it does |
|---|---|---|
| POST | `/api/auth/token` | Email + password → tokens |
| POST | `/api/auth/refresh` | Refresh token (body or cookie) → new tokens |
| POST | `/api/auth/revoke` | Sign out |
| GET | `/api/transactions` | Your transactions (`search`, `from`, `to`, `page`, `pageSize`) |
| GET | `/api/transactions/{id}` | One transaction and whether it can be disputed |
| POST | `/api/transactions/{id}/disputes` | Raise a dispute |
| GET | `/api/disputes` | Your dispute history |
| GET | `/api/disputes/{id}` | One dispute with its timeline |
| POST | `/api/disputes/{id}/withdraw` | Withdraw an open dispute |
| GET | `/api/agent/disputes` | Disputes team: the queue |
| POST | `/api/agent/disputes/{id}/start-review` | Disputes team: pick one up (assigns it to you) |
| POST | `/api/agent/disputes/{id}/resolve` | Disputes team: uphold or decline (reviewer only) |

Errors come back as standard problem details with a stable `code`. The OpenAPI document is at `/openapi/v1.json` in Development.

## How it's put together

```
src/
  DisputePortal.Domain/          Aggregates (Dispute, Transaction), value objects, domain events, business rules
  DisputePortal.Application/     Use-case handlers, ports and DTOs
  DisputePortal.Infrastructure/  EF Core, Identity, JWT and repositories
  DisputePortal.Api/             API controllers and startup; serves the built React app
  DisputePortal.Client/          React app: pages, API hooks, auth, component and end-to-end tests
tests/
  DisputePortal.UnitTests/       Domain, handlers, architecture rules
  DisputePortal.IntegrationTests/ Persistence and the API over HTTP
```

Dependencies only point inwards: **Api → Infrastructure → Application → Domain**. The React app talks to the Api over HTTP only.

The React app is in the solution as a JavaScript project (`DisputePortal.Client.esproj`), so Visual Studio shows it, starts it and runs its tests. It's marked not to build with the solution, so `dotnet build` and `dotnet test` don't need Node; npm builds it.

The `Dispute` aggregate enforces its own rules:
- only posted debits can be disputed
- they must be within 120 days
- only the account holder can raise one
- an agent must start a review before closing a dispute, and only that agent can close it
- a transaction gets one decision: once a dispute is upheld or declined, the customer can't raise another on it, and appeals go through the bank's complaints process. A withdrawn dispute doesn't count, so the customer can raise a new one.

Every change adds a timeline entry and raises a domain event. Events are saved to an outbox table in the same transaction as the change, then handled by a background worker with retries. A failing handler can't lose an event or turn a saved dispute into an error.

### The React app

- **Server state** is held in TanStack Query, which handles caching, loading and error states, and refetching after a change. There's no global store, because almost all state is server state.
- **Filters and paging** live in the URL, so they survive a reload and work with the back button.
- **Forms** use React Hook Form with Zod schemas. These apply the same limits as the server, so mistakes show up before a round trip. The server still has the final word, and its errors appear against the same fields.
- **Tokens:**
  - The access token is kept in memory only, never in storage.
  - The refresh token never reaches the page's JavaScript, because it lives in the HttpOnly cookie. After a reload, the app gets a fresh access token from that cookie.
  - The only thing in browser storage is a flag saying a session exists, so a first-time visitor isn't sent on a pointless refresh call.
  - On a 401 the app refreshes once and retries. Parallel requests share that one refresh.
- **Hosting:**
  - The back end serves the app from the same address as the API, so there's no CORS setup.
  - The strict Content-Security-Policy covers the app: no inline scripts and no third-party hosts.
  - Any address that isn't a file or an API route gets the app's `index.html`, so links to any page and reloads work.

## Configuration

Set anything in `appsettings.json` or with environment variables (`__` for nesting, for example `Jwt__AccessTokenMinutes=10`).

| Setting | Default | What it does |
|---|---|---|
| `Jwt:SigningKey` | *(required)* | Secret for signing tokens, 32+ characters |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | `15` / `7` | Token lifetimes |
| `ConnectionStrings:DefaultConnection` | `Data Source=data/disputeportal.db` | SQLite database location |
| `Disputes:WindowDays` | `120` | How long a transaction can be disputed for |
| `Portal:TimeZone` | `Africa/Johannesburg` | Time zone for dates |
| `DemoData:Enabled` / `DemoData:Password` | `false` / empty | Seed demo accounts |
| `OpenApi:Enabled` | `false` | Serve OpenAPI outside Development |

The refresh-token cookie is `Secure`, so outside localhost the portal must be served over HTTPS. Usually TLS ends at a reverse proxy; set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` behind one.

## What I'd do next

- Switch SQLite for SQL Server or PostgreSQL. Only Infrastructure would change.
- Add password reset to the React app, with a real email sender.
- Add receipt attachments and customer notifications on disputes.
- Let a supervisor reassign a dispute, and require a second person to approve upheld refunds.
- Support currencies that don't use two decimal places. Amounts currently assume cents, which is fine for ZAR.
