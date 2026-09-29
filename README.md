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

## Accounts

Statements are imported into accounts. Every database starts with three, one per kind: "Chequing",
"Savings" and "Credit card" (transactions stored before accounts existed are on them). Add more for a second
card or bank account, rename them, or delete one that has no transactions (deleted ones included: they are
what keeps a re-import from adding them back). An account's kind (`Chequing`, `Savings`, `CreditCard`) never
changes; it decides what its transactions mean when no flow rule says otherwise.

- `GET /api/Accounts`: `{id, name, type, format, transactionCount}`, by kind then name.
- `POST /api/Accounts` `{name, type, format?}`, `PUT /api/Accounts/{id}` `{name, format?}`: names are trimmed, up to
  100 characters, and unique ignoring case (409 otherwise). `format` is the layout of the bank's statement files,
  `Cibc` (the default), `Neo` or `Manual` (no statement files: transactions are added by hand, and importing into it
  is refused); it can be corrected later, the kind can't.
- `DELETE /api/Accounts/{id}`: 409 while it has transactions.

## Import bank statements

Upload a CIBC CSV export with the id of the account it belongs to:

```bash
curl -F file=@cibc_credit.csv -F accountId=5b0c4b8e-3f5e-4c1a-9d53-0c2f7a1e8a03 http://localhost:5096/api/Transaction/Import
# {"read":42,"added":40,"skipped":2}
```

Re-importing a statement, or an overlapping one, only adds rows that aren't stored yet; identical
purchases on the same day are kept. Every row is imported; categories and flows come from the rules stored in
the database (see [Category rules](#category-rules) and [Flows](#flows)).

Each account's format decides how its file is read, and a file in another format is refused with 400 and a message
saying so (a CIBC file has no header line; a Neo file has one), instead of being read as nothing.

A file that doesn't look like the account's is refused with 409 and `"code": "account-mismatch"`: a credit card
export (CIBC's carry the card number) imported into a bank account, or a statement most of whose rows (at least
3) are already stored on another account. Sending `confirmed=true` imports it anyway.

Rent is imported as paid: the payment on the chequing statement (e.g. `VALIANT RENTAL PAYMENT`), in the
Rent category by the category rules. It used to be generated instead (a fixed "Monthly Rent" row on the 1st
of every month a chequing statement covered, with the real payment dropped). Those rows are kept for the months
already stored, with their category locked. Importing a chequing statement that has a month's real rent payment
hides that month's "Monthly Rent" row, so rent counts once; it can be restored from the deleted transactions.

Statements are read as UTF-8, or as Windows-1252 when the file isn't valid UTF-8 (e.g. re-saved in Excel).
Transactions are duplicates only within the same account.

### Adding transactions by hand

`POST /api/Transaction` `{date, description, amount, transactionType, accountId, categoryId?, flow?, allowDuplicate?}`
adds one transaction, for accounts without an importer (statement format `Manual`) or a purchase that isn't on a
statement yet. The amount is positive and `transactionType` (`Debit` or `Credit`) says which way the money moved.
Without `categoryId` and `flow` the rules decide, as for an imported row; a given one is kept when the rules are
re-applied, and a flow that doesn't fit the direction is a 400. An identical transaction (same account, day, amount
and description) is a 409 unless `allowDuplicate`, for two purchases that really were the same.

### Neo Financial

Neo only exports its credit card (the chequing and savings accounts have no export, so use `Manual` for them). The
export starts with a header line and has one signed amount per row, negative for
money out. Columns are found by name (`Transaction Date`, `Description`, `Amount`; `Status` and `Posted Date` are
optional), so their order doesn't matter. The date is the transaction date, not the day it posted. Only `Posted` rows
are imported: a pending row shows up again once it posts, possibly worded differently. Runs of spaces in merchant
names ("CALGARY       CAN") are collapsed to one.

Neo card payments read "Payment Received - Thank you"; a flow rule makes them transfers, so paying the card doesn't
count as a refund.

## Flows

Every transaction has a flow, which decides where the reports count it:

| Flow | Meaning | Allowed for |
|---|---|---|
| `Spending` | counts as spending | money out |
| `Refund` | money back, reduces spending | money in |
| `Income` | counts as income | money in |
| `Transfer` | between your own accounts (a card payment, moving money to savings) | both |
| `Ignored` | not counted anywhere (e.g. a pending hold) | both |

A flow rule is a keyword and a flow; the rule with the longest keyword the description contains (ignoring case)
wins, and income and refund rules only apply to money in. Without a matching rule, money out is spending, money in
is income on chequing and a refund on a credit card, and everything on a savings account is a transfer.

- `GET /api/FlowRules`: `{id, keyword, flow}`, by keyword.
- `POST /api/FlowRules` `{keyword, flow}`, `PUT /api/FlowRules/{id}`, `DELETE /api/FlowRules/{id}`: keywords are
  stored trimmed and upper-case, up to 100 characters, and unique (409); a `Spending` rule is refused (400).

`RulePacks` in `import-rules.json` are extra starter sets, one per bank (`Neo` for now). Each is copied into the
database once, after the rules already there and skipping keywords that already have one, so a database seeded before
the pack existed gets it too, and nothing edited in the app is overridden (a rule the pack adds can only apply where
none of yours match first).

`FlowRules` in `import-rules.json` is only the initial set (transfers, card payments, pending holds, income
sources), copied into the database once like the category rules. Before flows existed, rows matching those
keywords were dropped at import, and so was money in on chequing that wasn't a known income; the migration
gives stored rows the flow the reports gave them (purchases spending, card credits refunds, chequing credits
income, savings transfers).

## Correcting transactions

`PUT /api/Transaction/{id}` `{description, categoryId, flow?}` changes a transaction's description, category and
flow (a flow the other way is refused with 400). A category or flow picked by hand is locked:
`POST /api/Transaction/ApplyRules` re-runs the current category and flow rules over everything else (hidden
rows are left alone, and so are the categories of savings accounts' rows). Editing never changes the duplicate
hash, so re-importing the statement still skips the row.

## Categories

Categories are data, edited in the app (the frontend's Categories page). Every database starts with the eleven there
were before that (Dining, Groceries, Other, Rent...), with fixed ids and the colours the app always gave them; a
migration moved the stored transactions, rules and budgets onto them.

- `GET /api/Categories`: `{id, name, color, transactionCount, ruleCount, isFallback, builtInReason}` by name
  (`isFallback` marks Other, whatever it is called now). The counts include
  deleted transactions, which move with the category too.
- `POST /api/Categories` `{name, color}`, `PUT /api/Categories/{id}` `{name, color}`: names are trimmed, up to 50
  characters and unique ignoring case (409 otherwise); `color` is `#RRGGBB`, stored upper-case. Renaming or
  recolouring shows everywhere at once: everything refers to the category by id.
- `DELETE /api/Categories/{id}?moveTo={id}`: moves its transactions (deleted ones included) and rules to `moveTo`
  (`Other` when omitted), then deletes it; a budget that counted it stops counting it. `Other` (where transactions no
  rule matches go) and `Rent` (whose payments replace the generated monthly rent) are built in: they can be renamed
  and recoloured, but deleting them is a 409 with the reason, also given as `builtInReason`.

Transactions, rules and budgets take categories by id (`categoryId`, `categoryIds`) and return them as
`{id, name, color}`. The reports list their categories the same way and key each month's values by category name.

## Category rules

A rule is a keyword and a category: a transaction whose description contains the keyword (ignoring case)
gets the category. Rules are checked in order and the first match wins; no match means `Other`.

The rules live in the database and are edited in the app (the frontend's Rules page), so a new merchant
needs no redeploy:

- `GET /api/CategoryRules`: all rules in order, as `{id, keyword, category, position}` (positions run 0..n-1).
- `POST /api/CategoryRules` `{keyword, categoryId, position?}`: adds a rule at the end, or inserts it at
  `position`. Keywords are stored trimmed and upper-case, up to 100 characters, and unique (409 otherwise).
- `PUT /api/CategoryRules/{id}` `{keyword, categoryId}`, `DELETE /api/CategoryRules/{id}`.
- `POST /api/CategoryRules/{id}/Move` `{position}`: moves a rule (past the end means last); returns all rules.
- `POST /api/CategoryRules/Preview` `{keyword, categoryId, excludeTransactionId?}`: how many stored transactions
  would change category if the rule were added first and the rules re-applied, with up to five examples.

Changed rules apply to later imports; `POST /api/Transaction/ApplyRules` applies them to stored
transactions. `CategoryRules` in `import-rules.json` is only the initial set: the API copies it into the
database (one rule per keyword, in order) the first time it starts on a database, and never again, even after
every rule was deleted. The file names categories; a name means the default category first called that (renamed or
not), and rules for a category deleted in the app are left out. The CLI has no database, so it still categorises
with the file's rules, into the default categories.

## Budgets

A budget's `period` is `Total` (everything linked counts, e.g. a trip) or `Monthly` (only transactions dated
in the current month count, so it starts over every month).

A monthly budget can also have categories (`categoryIds` on `POST`/`PUT`, returned as `categories`): it
then counts this month's spending in those categories by itself, with no linking. That is spending as the reports
count it (`Spending` minus `Refund`, so transfers and ignored rows stay out), deleted transactions excluded, and
transactions linked to another budget left to that one. A transaction linked to the budget and also in its
categories counts once. Only monthly budgets can have categories (400 otherwise), and a category belongs to one
budget at most (409 naming the budget that has it); deleting a budget frees its categories.

## Reports

Spending is every `Spending` transaction minus every `Refund`, whichever account it is on.
`GET /api/Reports/cash-flow` gives income, spending, net (income minus spending) and money saved (into savings
accounts minus out of them) per month of the current year, with monthly averages over the finished months.

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
