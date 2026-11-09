# Transactions Dispute Portal

A web app where bank customers look through their card transactions, dispute the ones that don't look right, and follow each dispute through to the outcome. A small back office lets the disputes team pick up disputes and resolve them.

It's built on .NET 10 with Domain-Driven Design and Clean Architecture, using only what the framework provides: ASP.NET Core MVC, EF Core (SQLite), Identity and the built-in JWT bearer handler. There's a website for customers and agents, and a JSON API secured with JWT access tokens and rotating refresh tokens.

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

Try this: as Amanda, search for **Kauai**. There are two identical charges a minute apart, so dispute one. Then sign in as Naledi, pick it up and uphold it. Back as Amanda, the history shows the outcome.

Demo data is off by default and is only seeded into an empty database.

## Run it without Docker

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/DisputePortal.Web
```

In Development, demo data and a development-only JWT key are switched on for you.

## Run the tests

```bash
dotnet test
```

There are 71 tests:

- **Unit tests** cover the domain rules, every use-case handler, and architecture checks that fail the build if the layering is broken.
- **Integration tests** cover persistence against a real SQLite database. They also boot the whole app to test the website, the API and the JWT flows.

To run them in Docker instead:

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

| Method | Path | What it does |
|---|---|---|
| POST | `/api/auth/token` | Email + password → tokens |
| POST | `/api/auth/refresh` | Refresh token → new tokens |
| POST | `/api/auth/revoke` | Sign out |
| GET | `/api/transactions` | Your transactions (`search`, `from`, `to`, `page`, `pageSize`) |
| GET | `/api/transactions/{id}` | One transaction and whether it can be disputed |
| POST | `/api/transactions/{id}/disputes` | Raise a dispute |
| GET | `/api/disputes` | Your dispute history |
| GET | `/api/disputes/{id}` | One dispute with its timeline |
| POST | `/api/disputes/{id}/withdraw` | Withdraw an open dispute |
| GET | `/api/agent/disputes` | Disputes team: the queue |
| POST | `/api/agent/disputes/{id}/start-review` | Disputes team: pick one up |
| POST | `/api/agent/disputes/{id}/resolve` | Disputes team: uphold or decline |

Errors come back as standard problem details with a stable `code`. The OpenAPI document is at `/openapi/v1.json` in Development.

## How it's put together

```
src/
  DisputePortal.Domain/          Aggregates (Dispute, Transaction), value objects, domain events, business rules
  DisputePortal.Application/     Use-case handlers, ports and DTOs
  DisputePortal.Infrastructure/  EF Core, Identity, JWT and repositories
  DisputePortal.Web/             MVC and API controllers, views and startup
```

Dependencies only point inwards: **Web → Infrastructure → Application → Domain**.

The `Dispute` aggregate enforces its own rules:
- only posted debits can be disputed
- they must be within 120 days
- one live dispute per transaction
- only the account holder can raise one

Every change adds a timeline entry and raises a domain event.

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

## What I'd do next

- Switch SQLite for SQL Server or PostgreSQL. Only Infrastructure would change.
- Plug in a real email sender. Password-reset emails go nowhere yet.
- Add receipt attachments and customer notifications on disputes.
