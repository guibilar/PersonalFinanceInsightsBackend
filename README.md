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
| `FinanceApi` | Controllers, mapping, error handling, `/health` | Infrastructure |
| `Finance.Cli` | Console tool: charts and Excel export from local statements | Infrastructure |
| `FinanceTests` | API tests (xUnit + `WebApplicationFactory`) | FinanceApi |

Package versions live in `Directory.Packages.props`; shared build settings (net8.0, nullable,
warnings as errors) in `Directory.Build.props`.

## Run locally

Requires the .NET 8 SDK and a PostgreSQL server on `localhost:5432` (user/password `postgres`,
as in `appsettings.Development.json`).

```bash
dotnet tool restore
dotnet ef database update --project Finance.Infrastructure --startup-project FinanceApi
dotnet run --project FinanceApi          # http://localhost:5096, Swagger UI at the root
```

The frontend's dev server proxies `/api` to `http://localhost:5096`.

## Import bank statements

Upload a CIBC CSV export with the account it belongs to (`CreditCard`, `Chequing` or `Savings`):

```bash
curl -F file=@cibc_credit.csv -F account=CreditCard http://localhost:5096/api/Transaction/Import
# {"read":42,"added":40,"skipped":2}
```

Re-importing a statement, or an overlapping one, only adds rows that aren't stored yet; identical
purchases on the same day are kept. Categories, excluded descriptions, income sources and the fixed
monthly rent are configured in `Finance.Infrastructure/Import/import-rules.json` (override with
`Import__...` environment variables).

Statements are read as UTF-8, or as Windows-1252 when the file isn't valid UTF-8 (e.g. re-saved in Excel).
Transactions are duplicates only within the same account.

## Reports

The dashboard's current month and year follow `Reports:TimeZone` in `appsettings.json`
(IANA id, default `America/Toronto`; override with `Reports__TimeZone`). An unknown id stops the API at startup.

## Tests

```bash
dotnet test                                                          # in-memory database
FINANCE_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres" dotnet test
```

With `FINANCE_TEST_POSTGRES` set, every test class gets its own freshly migrated PostgreSQL database
(dropped afterwards). CI runs both, plus a check that the migrations match the model and a Docker build.

## Docker

```bash
cp .env.example .env        # set POSTGRES_PASSWORD
docker compose up --build   # API on http://localhost:5096, migrations applied on start
```

`POSTGRES_PASSWORD` only takes effect when the database volume is first created; for an existing
volume, use the password it was created with.

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
