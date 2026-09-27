# Personal Finance Insights – API

ASP.NET Core 8 API behind the [Personal Finance Insights frontend](https://github.com/guibilar/PersonalFinanceInsightsFrontend):
imports bank statements, categorises spending, tracks budgets and serves the dashboard reports.
PostgreSQL via EF Core.

## Project layout

| Project | Contents | Depends on |
| --- | --- | --- |
| `Finance.Domain` | Entities, enums, domain exceptions, transaction hash | – |
| `Finance.Contracts` | Request/response DTOs, report models | Domain |
| `Finance.Application` | Services, repository/parser interfaces, import rules | Domain, Contracts |
| `Finance.Infrastructure` | EF Core `DbContext`, migrations, repositories, CIBC CSV parser | Application |
| `FinanceApi` | Controllers, mapping, error handling, health checks | Infrastructure |
| `Finance.Cli` | Console tool: charts and Excel export from local statements | Infrastructure |
| `FinanceTests` | API tests (xUnit + `WebApplicationFactory`) | FinanceApi |

Package versions live in `Directory.Packages.props`; shared build settings (net10.0, nullable,
warnings as errors) in `Directory.Build.props`.

## Run locally

Requires the .NET 10 SDK and a PostgreSQL server on `localhost:5432` (user/password `postgres`,
as in `appsettings.Development.json`).

```bash
dotnet tool restore
dotnet ef database update --project Finance.Infrastructure --startup-project FinanceApi
dotnet run --project FinanceApi          # http://localhost:5096, Swagger UI at the root
```

The frontend's dev server proxies `/api` to `http://localhost:5096`.

## Signing in

Every endpoint except `POST /api/Auth/login`, `POST /api/Auth/logout` and the health checks needs a
signed-in user. Signing in sets an HttpOnly, SameSite=Strict session cookie (`finance_session`); the
frontend reaches the API on the same origin (nginx in production, the dev-server proxy locally).

- **Accounts:** there is no public sign-up. `Auth:InitialUser:Username` / `Password` create an account at
  startup if it doesn't exist (Docker: `FINANCE_USERNAME` / `FINANCE_PASSWORD` in `.env`). In Development
  the account is `dev` / `development-only`. Passwords need at least 12 characters.
- **Forgotten password:** start once with `Auth:InitialUser:ResetPassword=true` (`FINANCE_RESET_PASSWORD=true`),
  then remove it. It sets the configured password and clears a lockout.
- **Lockout:** 5 wrong passwords lock the account for 15 minutes.
- **Data:** everyone who can sign in sees the same transactions and budgets.
- Other endpoints: `GET /api/Auth/me`, `POST /api/Auth/change-password`.

The keys that encrypt the cookie are stored in the database, so sessions survive a container restart.

**Behind a reverse proxy:** the API believes `X-Forwarded-Proto` / `X-Forwarded-For` only from proxies on
`ReverseProxy:TrustedNetworks` (loopback plus the private ranges by default, which covers the frontend's
nginx container). When the browser connects over HTTPS somewhere in front, the session cookie is marked
`Secure`. Narrow the list (e.g. `ReverseProxy__TrustedNetworks__0=172.18.0.0/16`) if other machines on
your network can reach the API directly.

## Import bank statements

Upload a CIBC CSV export with the account it belongs to (`CreditCard`, `Chequing` or `Savings`):

```bash
curl -F file=@cibc_credit.csv -F account=CreditCard http://localhost:5096/api/Transaction/Import
# {"read":42,"added":40,"skipped":2}
```

Re-importing a statement, or an overlapping one, only adds rows that aren't stored yet; identical
purchases on the same day are kept. Categories, excluded descriptions, income sources, card payments and
the fixed monthly rent are configured in `Finance.Infrastructure/Import/import-rules.json` (override with
`Import__...` environment variables).

Money in on a credit card statement is imported as a refund (a credit in the purchase's category), except
the card's own payments, recognised by `CardPaymentKeywords`. Refunds reduce spending in the reports and
in budgets. On chequing, money in only counts as income when it matches `IncomeKeywords`.

Statements are read as UTF-8, or as Windows-1252 when the file isn't valid UTF-8 (e.g. re-saved in Excel).
Transactions are duplicates only within the same account.

## Correcting categories

`PUT /api/Transaction/{id}` changes a transaction's description and category. A category picked by hand is
locked: `POST /api/Transaction/ApplyCategoryRules` re-runs the current rules from `import-rules.json` over
everything else (savings rows and the fixed monthly rent are left alone). Editing never changes the duplicate
hash, so re-importing the statement still skips the row.

## Budgets

A budget's `period` is `Total` (everything linked counts, e.g. a trip) or `Monthly` (only transactions dated
in the current month count, so it starts over every month).

## Reports

The dashboard's current month and year follow `Reports:TimeZone` in `appsettings.json`
(IANA id, default `America/Toronto`; override with `Reports__TimeZone`). An unknown id stops the API at startup.

## Tests

xUnit v3 on Microsoft Testing Platform (enabled in `global.json`), so `dotnet test` runs the test project
as an executable.

```bash
dotnet test                                    # in-memory database (fast; 4 PostgreSQL-only tests are skipped)
FINANCE_TEST_POSTGRES=container dotnet test    # starts PostgreSQL 15 in Docker (Testcontainers), removes it afterwards
FINANCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres" dotnet test   # your own server
dotnet test -- --filter-class FinanceTests.AuthTests                                  # one class
```

On PostgreSQL every test factory gets its own freshly migrated database, dropped afterwards. CI runs
the in-memory and container modes, plus a check that the migrations match the model and a Docker build.

## Docker

```bash
cp .env.example .env        # set POSTGRES_PASSWORD and FINANCE_USERNAME / FINANCE_PASSWORD
docker compose up --build   # API on http://localhost:5096, migrations applied on start
```

`POSTGRES_PASSWORD` only takes effect when the database volume is first created; for an existing
volume, use the password it was created with.

PostgreSQL is published on `127.0.0.1:5432` only: reachable from this machine (psql, `dotnet run`),
not from the rest of the network.

Health checks: `/health/live` answers while the process runs (use it for restarts);
`/health/ready` (and its alias `/health`) also checks the database.

Outside Development the connection string must come from the environment
(`ConnectionStrings__DefaultConnection`); the API refuses to start without it.
Swagger UI and CORS are only enabled in Development.

## Database migrations

```bash
dotnet ef migrations add <Name> --project Finance.Infrastructure --startup-project FinanceApi
```

Enums are stored as text. Check the generated SQL of any migration that changes a column type:
EF's default conversions don't know about enum names (see `StoreEnumsAsStrings`).

## CLI

Put `cibc_credit.csv`, `cibc_debit.csv` and `cibc_savings.csv` in `Finance.Cli/Files/CIBC/`
(git-ignored), then `dotnet run --project Finance.Cli`. Charts and the Excel report are written to
`Results/` next to the binary.
