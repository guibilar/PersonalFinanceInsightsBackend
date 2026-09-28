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
- **Sessions:** kept in the database (`UserSessions`); the cookie only carries the session's key. Signing out
  ends the session on the server, so a copy of the cookie stops working too. Changing the password signs
  out every other session at once, and `POST /api/Auth/sign-out-others` does the same on demand
  (answering how many ended).
- Other endpoints: `GET /api/Auth/me`, `POST /api/Auth/change-password`.

The keys that encrypt the cookie and the sessions are stored in the database, so sessions survive a
container restart.

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
purchases on the same day are kept. Excluded descriptions, income sources and card payments are configured
in `Finance.Infrastructure/Import/import-rules.json` (override with
`Import__...` environment variables). Categories come from the category rules stored in the database
(see [Category rules](#category-rules)).

Money in on a credit card statement is imported as a refund (a credit in the purchase's category), except
the card's own payments, recognised by `CardPaymentKeywords`. Refunds reduce spending in the reports and
in budgets. On chequing, money in only counts as income when it matches `IncomeKeywords`.

Rent is imported as paid: the payment on the chequing statement (e.g. `VALIANT RENTAL PAYMENT`), in the
Rent category by the category rules. It used to be generated instead (a fixed "Monthly Rent" row on the 1st
of every month a chequing statement covered, with the real payment dropped). Those rows are kept for the months
already stored, with their category locked. Importing a chequing statement that has a month's real rent payment
hides that month's "Monthly Rent" row, so rent counts once; it can be restored from the deleted transactions.

Statements are read as UTF-8, or as Windows-1252 when the file isn't valid UTF-8 (e.g. re-saved in Excel).
Transactions are duplicates only within the same account.

## Correcting categories

`PUT /api/Transaction/{id}` changes a transaction's description and category. A category picked by hand is
locked: `POST /api/Transaction/ApplyCategoryRules` re-runs the current category rules over everything else
(savings rows and deleted rows are left alone). Editing never changes the duplicate
hash, so re-importing the statement still skips the row.

## Category rules

A rule is a keyword and a category: a transaction whose description contains the keyword (ignoring case)
gets the category. Rules are checked in order and the first match wins; no match means `Other`.

The rules live in the database and are edited in the app (the frontend's Rules page), so a new merchant
needs no redeploy:

- `GET /api/CategoryRules`: all rules in order, as `{id, keyword, category, position}` (positions run 0..n-1).
- `POST /api/CategoryRules` `{keyword, category, position?}`: adds a rule at the end, or inserts it at
  `position`. Keywords are stored trimmed and upper-case, up to 100 characters, and unique (409 otherwise).
- `PUT /api/CategoryRules/{id}` `{keyword, category}`, `DELETE /api/CategoryRules/{id}`.
- `POST /api/CategoryRules/{id}/Move` `{position}`: moves a rule (past the end means last); returns all rules.
- `POST /api/CategoryRules/Preview` `{keyword, category, excludeTransactionId?}`: how many stored transactions
  would change category if the rule were added first and the rules re-applied, with up to five examples.

Changed rules apply to later imports; `POST /api/Transaction/ApplyCategoryRules` applies them to stored
transactions. `CategoryRules` in `import-rules.json` is only the initial set: the API copies it into the
database (one rule per keyword, in order) the first time it starts on a database, and never again, even after
every rule was deleted. The CLI has no database, so it still categorises with the file's rules.

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
the in-memory and container modes, a check that the migrations match the model, and
`scripts/check-vulnerable-packages.sh` (known advisories of moderate severity or worse in the packages
the API and CLI ship with; it also runs weekly). It then starts the Docker stack with
`docker compose` and runs `docker/smoke-test.sh` against it: health, sign-in, the session cookie's
flags and a protected call. You can run the smoke test against your own deployment too, on the server:
`FINANCE_USERNAME=... FINANCE_PASSWORD=... docker/smoke-test.sh` (it signs in and out once).

## Docker

```bash
cp .env.example .env        # set POSTGRES_PASSWORD and FINANCE_USERNAME / FINANCE_PASSWORD (and BACKUP_UID/GID)
mkdir -p backups            # before the first start, so it belongs to you (see Backups)
docker compose up --build   # API on http://localhost:5096 (this machine only), migrations applied on start
```

Start this project before the frontend's: it creates the `finance` Docker network that the frontend
joins. Browsers never talk to the API directly: they reach the frontend over HTTPS (Tailscale, see the
frontend README), and its nginx calls the API as `http://personal-finance-api:8080` on that network.
The API port is published on `127.0.0.1:5096` only, so nothing on your LAN can reach it, and in
particular no password crosses the network unencrypted.

`POSTGRES_PASSWORD` only takes effect when the database volume is first created; for an existing
volume, use the password it was created with.

PostgreSQL is published on `127.0.0.1:5432` only: reachable from this machine (psql, `dotnet run`),
not from the rest of the network.

Health checks: `/health/live` answers while the process runs (Docker's health check for the API
container uses it); `/health/ready` (and its alias `/health`) also checks the database.

Container logs are capped at 3 files of 10 MB per service (Docker keeps them forever by default).

### Backups

The `backup` service dumps the database when the stack starts and then every night at `BACKUP_AT`
(03:00, Toronto time), into `./backups` (`BACKUP_DIR`) as `finance-<date>_<time>.dump`. Dumps older
than `BACKUP_KEEP_DAYS` (14) are deleted after each successful one. `docker compose ps` shows the
container as unhealthy when the newest dump is more than 26 hours old, and `docker compose logs backup`
says why.

The container runs as your user (`BACKUP_UID`/`BACKUP_GID` in `.env`, from `id -u` and `id -g`), so the
dumps belong to you and are readable by you alone (mode 600). Create the folder before the first start,
or Docker creates it as root and the container can't write to it (`docker compose logs backup` then
says how to fix it):

```bash
mkdir -p backups
# Upgrading from dumps written by root: sudo chown -R "$(id -u):$(id -g)" backups && chmod 600 backups/*.dump
```

**The dumps are sensitive.** Each one is the whole database: every transaction and statement, the
account's password hash, the keys that encrypt the session cookie, and the open sessions. Anyone with a
dump can read your financial history, and the cookie keys and sessions in it can pass for a signed-in
browser.

**Copy the folder off this machine** (another disk, a NAS, cloud storage) on a schedule of your own:
a backup on the same disk doesn't survive that disk. **Encrypt the copy.** With
[age](https://age-encryption.org):

```bash
# Once: a key pair. Then move finance-backup-key.txt off this machine (a password manager, a USB key):
# whoever has it can read the backups, and without it nobody can, you included.
age-keygen -o finance-backup-key.txt          # prints the public key, age1...
# Each copy: only the public key is needed here.
for f in backups/*.dump; do age -r age1... -o "/mnt/offsite/$(basename "$f").age" "$f"; done
# To restore: decrypt, then restore.sh
(umask 077; age -d -i finance-backup-key.txt -o finance.dump /mnt/offsite/finance-2026-09-28_030000.dump.age)
docker/restore.sh finance.dump
```

A destination that encrypts by itself works too: an rclone `crypt` remote, or an encrypted disk
(LUKS, BitLocker, FileVault). An unencrypted cloud folder or NAS share is not enough.

```bash
docker compose exec backup backup.sh once          # an extra dump right now
docker/restore.sh backups/finance-2026-09-28_030000.dump
```

`docker/restore.sh` replaces the current database with the dump: it first saves the current database
as `backups/before-restore-<time>.dump` (mode 600, kept until you delete it), stops the API, restores in
a single transaction (a failure leaves the database as it was) and starts the API again. The dumps
include the account and the session-cookie keys, so you sign in as before. They are pg_dump's custom
format; `pg_restore --list <file>` shows what one contains.

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
