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
what keeps a re-import from adding them back). An account's kind (`Chequing`, `Savings`, `CreditCard`,
`Investment`) never changes; it decides what its transactions mean when no flow rule says otherwise. Savings and
investment movements are transfers by default, keep their category, and make the cash-flow report's savings change.

- `GET /api/Accounts`: `{id, name, type, format, transactionCount, lastTransactionDate, plan, contributionRoom,
  contributionRoomYear, depositedThisYear, withdrawnThisYear, roomLeft, balance}`, by kind then name.
  `lastTransactionDate` (the latest row, deleted ones included) and the latest balance's date say how far the account's
  imported statements reach.
- `POST /api/Accounts` `{name, type, format?, plan?, contributionRoom?, contributionRoomYear?}`, `PUT /api/Accounts/{id}`
  (the same without `type`): names are trimmed, up to 100 characters, and unique ignoring case (409 otherwise).
  `format` is the layout of the bank's statement files, `Cibc` (the default), `Neo`, `Wealthsimple` or `Manual` (no statement files:
  transactions are added by hand, and importing into it is refused); it can be corrected later, the kind can't. Fields
  left out of a `PUT` stay as they are.
- `DELETE /api/Accounts/{id}`: 409 while it has transactions; its balances go with it.

### Registered plans and contribution room

A savings or investment account can be held in a registered plan, `plan`: `Tfsa`, `Rrsp` or `Fhsa` (`None` clears it,
and its room; other kinds are a 400). `contributionRoom` is the plan's room for `contributionRoomYear` (this year when
left out; 2009 to this year), as the CRA's My Account shows it: for a TFSA, the room on January 1. The account then
says this year's transfers in (`depositedThisYear`: the contributions) and out (`withdrawnThisYear`: for a TFSA, room
again next January), and `roomLeft`: this year's room less this year's deposits, or null until this year's room is
entered. A transfer straight from or to another plan of the same kind (Wealthsimple's `TRFINTF`, `TRFOUTTF` and
`WIREINTF` rows) is neither: it uses no room and gives none back.

### Balances and net worth

A balance is what an account holds at the end of a day, as its bank or broker shows it (for a card, what it owes):

- `GET /api/Accounts/{id}/Balances`: newest first.
- `POST /api/Accounts/{id}/Balances` `{date, amount}`: one per day, so entering another for the day replaces it; not in
  the future (400), though tomorrow is taken as today (a device east of `Reports:TimeZone` can already be there).
- `DELETE /api/Accounts/{id}/Balances/{balanceId}`.

`GET /api/Reports/net-worth` gives each account's balance today, the assets, what the cards owe and the net worth, and
the same at the end of each of the last twelve months. After a balance, the account's transactions carry it on (not the
ignored ones); before its first one, a bank account's or card's transactions carry it back to just before the first
transaction imported. An investment's value also moves with the market, so it is known only from its first balance, and
only money in and out of it (transfers) moves it between balances: enter its balance now and then. `movedSince` says a
balance today includes transactions after the last one entered.

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

Neo exports CSV for its credit card and for Everyday and Savings accounts (from the website: Download CSV). The
export starts with a header line and has one signed amount per row, negative for
money out. Columns are found by name (`Transaction Date`, `Description`, `Amount`; `Status` and `Posted Date` are
optional), so their order doesn't matter. The date is the transaction date, not the day it posted. Only `Posted` rows
are imported: a pending row shows up again once it posts, possibly worded differently. Runs of spaces in merchant
names ("CALGARY       CAN") are collapsed to one.

Neo card payments read "Payment Received - Thank you"; a flow rule makes them transfers, so paying the card doesn't
count as a refund.

Neo's monthly **PDF statements** (Savings, Everyday, Chequing: in the app, Accounts → the account → Manage →
Statements) import into a `Neo` account too, so months the CSV doesn't reach can be filled in. The file is recognised
as a PDF by its first bytes. Rows come from the transactions table (the transaction date, with the year from the
statement's period, so a December row in a statement running into January keeps its year; the description, wrapped
lines joined; credits as money in and the negative debits as money out). Each row must take the previous balance to
its own and the last to the closing balance, starting from the summary's opening balance: when they don't, the
statement is refused (400, naming the row) and nothing is imported. As with CSV files, rows already imported are
skipped, so overlapping months add nothing twice. Paying the Neo card from Everyday ("Payment to Credit") is a
transfer.

### Wealthsimple

Wealthsimple has no export, but its monthly **PDF statements** (Documents → Statements: TFSA, RRSP, FHSA,
non-registered) import into a `Wealthsimple` account. Rows come from the Activity table: the date, the statement code
(`CONT`, `BUY`, `DIV`, …; the list is on the statement's last page) and the description, imported as one description
starting with the code ("CONT Contribution (executed at 2026-03-02)"), the debit as money out and the credit as money
in. Rows that move no cash (a stock dividend) are left out. Each row must take the cash balance to its own, from the
summary's last statement cash balance to its closing cash balance, or the statement is refused and nothing is
imported.

Only cash moves, so the code decides what a row means, and the rules can't change it: deposits, contributions and
withdrawals (`CONT`, `DEP`, `TRFIN`, `TRFINTF`, `WIREIN`, `WIREINTF`, `E_TRFIN` for an Interac e-Transfer in, `WD`, `WDQ`,
`TRFOUT`, `TRFOUTTF`, `E_TRFOUT`, `HBP`, `LLP`, `OBP`) follow the flow rules like any other (transfers, for an investment account); fees and taxes are spending;
bonuses and grants are income; everything else (buying and selling, dividends, interest, currency exchange) stays inside
the account and is ignored. The statement's portfolio value ("Total Portfolio", at market value) becomes the account's
balance on the period's last day, replacing one entered for that day; the import's response says it (`balance`,
`balanceDate`), so a month without activity still moves the net worth.

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

`PUT /api/Transaction/{id}` `{description, categoryId, flow?, note?, tagIds?}` changes a transaction's description,
category and flow (a flow the other way is refused with 400), its note (up to 500 characters; empty clears it, left
out keeps it) and its tags (the full set; left out keeps them, a tag that doesn't exist is a 400). A category or flow picked by hand is locked:
`POST /api/Transaction/ApplyRules` re-runs the current category and flow rules over everything else (hidden
rows are left alone, and so are the categories of savings accounts' rows). Editing never changes the duplicate
hash, so re-importing the statement still skips the row.

### Notes and tags

Tags are free labels across categories ("Vacation 2026", "Tax deductible"): `GET /api/Tags` lists them by name
with how many visible transactions carry each, `POST /api/Tags` `{name}` adds one (201; a name already used,
ignoring case, is a 409), `PUT /api/Tags/{id}` renames it and `DELETE /api/Tags/{id}` removes it from every
transaction (204). Names are trimmed, single-spaced and up to 40 characters.

Transactions carry `note` and `tags` (`{id, name}`, by name). `GET /api/Transaction?tagId=` keeps those with a tag,
and so do the breakdown and the reports' transaction criteria; the description search also looks in notes. A
`note` can be given when adding a transaction by hand.

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

A budget's `period` says which transactions count, by their date: `Total` (everything linked counts, e.g. a trip),
`Monthly` (the current month, so it starts over every month), `Yearly` (the current calendar year) or `Custom` (from
`startDate` to `endDate`, both days included; both are required for it and ignored for the other periods). "Current"
follows `Reports:TimeZone`.

A budget with a period other than `Total` can also count spending by itself, with no linking, by **category**
(`categoryIds` on `POST`/`PUT`, returned as `categories`) or by **merchant** (`merchant`: part of the description,
ignoring case, so `no frills` counts every store of the chain). That is the period's spending as the reports count it
(`Spending` minus `Refund`, so transfers and ignored rows stay out), deleted transactions excluded, and transactions
linked to another budget left to that one. A transaction linked to the budget and also matching counts once.

A budget counts categories or a merchant, not both, and a `Total` budget can have neither (400). A category belongs to
one budget at most (409 naming the budget that has it; deleting a budget frees its categories). A merchant can be in
several budgets, e.g. "No Frills" a month and a year.

## Reports

Spending is every `Spending` transaction minus every `Refund`, whichever account it is on.
`GET /api/Reports/cash-flow` gives, per month of the current year: `income` (`Income` flows), `spending`, `net` (income
minus spending: what was left over, negative when more went out than came in) and `savingsChange` (money into savings
accounts minus money out of them, interest included; 0 when nothing is recorded on a savings account, which is not the
same as nothing being saved, so `net` is the number to read for "how much did I keep"). A month is listed when
something in it counts (income, spending or refunds, or a savings account row); a month of nothing but transfers and
ignored rows isn't a month. The averages (`averageMonthly…`) are over the finished months, or over the running one
while none is finished; `averagedMonths` and `averagesIncludeCurrentMonth` say which. Which month is running follows
`Reports:TimeZone`.

`POST /api/Reports/breakdown` `{description?, categoryId?, accountId?, flow?, startDate?, endDate?, interval}` totals
what the transaction list's criteria pick (a merchant by part of its description, a category, or both; dates
inclusive) and spreads it over periods: `interval` is `Week` (from Monday), `Month` (the default), `Quarter` or `Year`.
It returns the count, spending, refunds and income; one entry per period from the first to the last (the first
matching transaction and today when no dates are given), empty ones included, with running totals; averages per
period over the finished ones; and the top 8 merchants, grouped by the category rule keyword that matched ("NO FRILLS"
for every store of the chain) or by description, with `merchantCount` for all of them. More than 400 periods are charted by the
next longer interval that fits (the response's `interval` says which); 400 years, or an end before the start, is a 400.

`POST /api/Reports/merchants` takes the same criteria with `page` and `pageSize` (default 8) and pages through all of
those merchants, largest first, as a paginated list: page 1 is the breakdown's top 8.

### Recurring payments, the forecast, what no rule sorts, reviews

`GET /api/Reports/recurring` finds payments that come again and again in the last two years of spending: a merchant
(its category rule's keyword, or the start of its description: "SQ *CORNER STORE #12" is "CORNER STORE") charged at a
steady interval (weekly, every two weeks, monthly, quarterly, yearly; three of every four intervals must fit, a missing
charge allowed) for a steady amount (within 15% for weekly ones, 35% for the rest, so weekly groceries don't pass). A
merchant billing several (an app store) is looked at price by price. Each has its `amount` (the latest for a fixed
price, the average of the last three when it varies, with `variableAmount`), `nextDate`, `monthlyCost` and
`yearlyCost`, a `priceChange` `{from, to, since}` when a fixed price changed with the latest charge, and a `status`:
`Active`, `Late` (past due, not by a whole period yet) or `Stopped` (listed for a year after the last charge). The
totals are of the active and late ones.

`GET /api/Reports/forecast` projects this month: what is `spent` so far, the recurring payments still due (`upcoming`),
and `dailyRate` (day-to-day spending a day over the last three complete months, recurring payments left out; this
month's pace before there is a complete month) for the days left. `usual` is the average of those months. Active
monthly and yearly budgets that count by category or merchant get a `projected` end too, at their own pace over those
months; ones that only count what is linked aren't projected.

`GET /api/Reports/uncategorised` lists the merchants whose spending was left in Other because no rule names them (not
picked by hand, not on a savings account), most transactions first, each with a `keyword` to make a rule of.

`GET /api/Reports/review?year=&month=` looks back on a month (or a year, without `month`; the last complete month
without either) against the one before: spending and income, purchases, days without one, every category with both
periods' spending, the top 5 merchants, the biggest purchase, each month of a year, the recurring payments as of
today, and `earliest` (the first month with data). A period that hasn't started is a 400.

`POST /api/Transaction/Bulk` `{transactionIds, categoryId?, flow?, budgetId?, unlinkBudget?, addTagId?, removeTagId?}`
(up to 500) sets a category, flow or budget, or adds or removes a tag, on many at once, with the same rules as one by one: the category and flow are kept from the
rules, a flow only where it fits the direction (the others keep theirs, and `notes` say how many), and only an active
budget (409), moving any linked to another.

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
