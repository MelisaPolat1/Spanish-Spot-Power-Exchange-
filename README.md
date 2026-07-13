# Spanish Spot Power Exchange — Day-Ahead Auction Results

A .NET 10 solution that periodically ingests Day-Ahead electricity auction
results from **OMIE** (the Iberian/MIBEL power exchange, covering Spain and
Portugal), persists them, and exposes them through a REST API and a Blazor
Server frontend.

## Overview

Every day, OMIE runs a Day-Ahead auction at 12:00 CET that sets electricity
prices for the following day, in 15-minute periods (96 periods/day since the
SDAC 15-minute Market Time Unit reform of 1 October 2025; hourly before
that). This application:

- Downloads the published results (`marginalpdbc_*.1` files) from OMIE's
  public file repository on a schedule, using Quartz.NET
- Persists them to a local SQLite database, with duplicate-import protection
- Exposes the stored data through a REST API, as a CET time series, in both
  JSON and semicolon-separated CSV
- Displays the results in a Blazor Server frontend with a chart and table

## Architecture

```
SpanishSpotPowerExchange/
├─ src/
│  ├─ SpotPower.Core            Domain entities, repository interface,
│  │                             shared Europe/Madrid timezone helper.
│  │                             No dependencies.
│  ├─ SpotPower.Infrastructure   EF Core (SQLite), OMIE file client,
│  │                             file parser, Quartz import job.
│  │                             Depends on Core.
│  ├─ SpotPower.Api              REST API + Quartz host. This is the
│  │                             long-running process: it hosts the
│  │                             scheduler in-process alongside the HTTP
│  │                             server. Depends on Infrastructure + Core.
│  └─ SpotPower.Web              Blazor Server frontend. Depends on
│                                 NOTHING from the backend — it only
│                                 knows the REST API's HTTP contract.
└─ ai-transcripts/               Complete, unedited AI conversation
                                  transcripts from research and
                                  implementation.
```

**Why Web has no project reference to Core/Infrastructure/Api:** this is a
deliberate structural constraint, not just a convention. The Blazor project
is physically incapable of touching the database or application services
directly — it can only reach data through the REST API over HTTP, which is
what the task requires.

**Why Quartz runs inside the API process rather than a separate Worker:**
the API is already a long-running process (an ASP.NET Core host), so hosting
the scheduler alongside it via `AddQuartzHostedService` satisfies "long-running
application" without a fifth project. Trade-off: the API and the ingestion
schedule share a process lifecycle — restarting one restarts the other. Given
the import job re-derives its own progress from the database on every run
(see below), this has no correctness impact, only an operational one.

## Data source

- **Publisher:** OMIE (OMI-Polo Español, S.A.), the Nominated Electricity
  Market Operator for Spain and Portugal.
- **File used:** `marginalpdbc_{yyyyMMdd}.1` — Day-Ahead market hourly/
  quarter-hourly prices in Spain and Portugal.
- **Access:** OMIE's public file repository
  (`omie.es/en/file-download?parents[0]=marginalpdbc&filename=...`), no
  authentication required.
- **Format:** semicolon-delimited text. One header line, one row per period
  (`Year;Month;Day;Period;PriceSpain;PricePortugal;`), one trailing `*`
  terminator line.

This project is not affiliated with or endorsed by OMIE. Data is public
information published by OMIE; see omie.es for terms of use.

## Prerequisites

- .NET 10 SDK
- (Optional) [DB Browser for SQLite](https://sqlitebrowser.org/) or the
  `sqlite3` CLI, for inspecting the local database
- Internet access to `www.omie.es` (no API key/token required)

## Getting started

```powershell
git clone <this-repo-url>
cd SpanishSpotPowerExchange
dotnet restore
dotnet build
```

Run the API (applies pending EF Core migrations automatically on startup,
and fires an immediate import on first run):

```powershell
dotnet run --project src/SpotPower.Api
```

In a second terminal, run the Blazor frontend:

```powershell
dotnet run --project src/SpotPower.Web
```

Navigate to the Web app's printed URL, then to `/day-ahead-prices`.

### Configuration

`src/SpotPower.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "SpotPowerDb": "Data Source=SpotPower.db"
  },
  "Omie": {
    "BaseUrl": "https://www.omie.es",
    "InitialBackfillDays": 7,
    "CronSchedule": "0 5 13 * * ?"
  }
}
```

- `InitialBackfillDays` — on an empty database, how many days of history to
  seed on first run (in addition to today and tomorrow).
- `CronSchedule` — Quartz cron expression for the recurring import. Pinned
  to **Europe/Madrid** time explicitly in `Program.cs` (`.InTimeZone(...)`),
  so `13:05` always means 13:05 Madrid time regardless of the host
  machine's own timezone — correct through CET/CEST transitions, and
  correct whether this runs on a Windows dev machine or a UTC-configured
  container.

`src/SpotPower.Web/appsettings.json`:

```json
{
  "Api": {
    "BaseUrl": "http://localhost:5077"
  }
}
```

Set this to wherever `SpotPower.Api` is actually running.

## REST API

### `GET /api/day-ahead-prices`

| Query param | Type | Default | Notes |
|---|---|---|---|
| `fromDate` | `yyyy-MM-dd` | today | Delivery date, inclusive |
| `toDate` | `yyyy-MM-dd` | `fromDate` | Delivery date, inclusive |
| `zone` | `Spain` \| `Portugal` | `Spain` | Case-insensitive |

Response format is chosen via the `Accept` header:

**`Accept: application/json`** (default):
```json
[
  {
    "periodStartCet": "2026-07-10T00:00:00+02:00",
    "durationMinutes": 15,
    "zone": "Spain",
    "priceEurPerMwh": 160.00
  }
]
```

**`Accept: text/plain`** — semicolon-separated CSV, fixed 2-decimal prices:
```
PeriodStartCet;DurationMinutes;Zone;PriceEurPerMwh
2026-07-10T00:00:00+02:00;15;Spain;160.00
2026-07-10T00:15:00+02:00;15;Spain;154.65
```

All timestamps are returned as `DateTimeOffset` values with an explicit
`+01:00` (CET) or `+02:00` (CEST) offset — the response is always an
unambiguous CET/CEST time series, never a bare local time.

Example:
```powershell
Invoke-RestMethod "http://localhost:5077/api/day-ahead-prices?fromDate=2026-07-10&toDate=2026-07-10&zone=Spain" -Headers @{ Accept = "text/plain" }
```

> **Note on decimal display in the Blazor UI:** the API's JSON/CSV output
> always uses a dot decimal separator (invariant culture), as shown above.
> The Blazor frontend's table may display prices with a comma (e.g. `145,85`)
> instead, depending on the browser/OS locale — this is a client-side
> number-formatting behavior of the browser, not a difference in what the
> API actually returns.

## Design notes

### Duplicate-import avoidance

Rows are keyed by the **exact UTC instant** (`PeriodStartUtc`) plus `Zone`,
never by calendar date. This matters specifically because Spain's local time
is ahead of UTC (CET/CEST = UTC+1/+2): periods early in the Spanish delivery
day fall on the *previous* UTC calendar date. Comparing by calendar day
caused real duplicate-insert failures during development; comparing by exact
instant does not, since two instants are either equal or they aren't,
regardless of which calendar day either one happens to land on in any given
timezone.

This is enforced at two levels:
1. The import job checks existing `(PeriodStartUtc, Zone)` pairs in bulk
   before inserting, skipping ones already present.
2. A unique database index on `(PeriodStartUtc, Zone)` is the backstop —
   even if the in-memory check had a bug, the database itself cannot
   physically store a duplicate row.

### Restart resilience

- EF Core migrations are applied automatically on every API startup.
- The import job determines its own starting point on every run — the day
  after the latest `DeliveryDate` already stored, or `today − InitialBackfillDays`
  if the database is empty — rather than relying on any in-memory state.
  A restart simply resumes; it does not require special recovery logic.
- Quartz is configured with two triggers on the same job: a recurring cron
  trigger for the daily schedule, and a one-shot trigger that fires
  immediately on every startup (a `CronTrigger`'s own `StartNow()` does
  *not* mean "fire immediately" — only a plain `SimpleTrigger` does — so a
  separate trigger is needed to actually catch up right away after a
  restart rather than waiting for the next scheduled tick).

### DST handling

Period-to-UTC conversion explicitly handles both DST edge cases in
`Europe/Madrid`:
- **Spring-forward gap** (the 02:00–03:00 local hour that doesn't exist) —
  should not normally occur, since OMIE's own period count already omits
  that hour on the short day, but handled defensively.
- **Autumn fallback** (the 02:00–03:00 local hour that occurs twice) — the
  first occurrence in a day's period sequence uses the CEST (summer)
  offset, the second uses the CET (winter) offset, tracked via the
  period-processing order within the parser.

## Manual verification performed

- Parsed OMIE data cross-checked line-by-line against manually downloaded
  raw files for multiple dates, including a date where Spain/Portugal
  prices genuinely diverge (confirming the column-to-zone mapping is not
  swapped).
- Record counts validated against expected period counts (96/day ×
  2 zones), matching exactly across a 9-day backfill window (7-day
  configured backfill + today + tomorrow).
- Both JSON and CSV response formats tested against live requests with
  explicit `Accept` headers, not just read from source code.
- End-to-end Blazor → API → database flow confirmed with real data,
  including CET/CEST offset correctness across the chart and table.

## Assumptions and limitations

- **OMIE file format**: the parser assumes columns
  `Year;Month;Day;Period;PriceSpain;PricePortugal;`. This has been spot-checked
  against real downloaded files, including a day where Spain and Portugal
  prices genuinely diverge (confirming the column-to-zone mapping is not
  swapped), but has **not** been verified against OMIE's own published
  file-format specification document
  (`omie.es/en/publicaciones/64`). If OMIE changes this format without
  notice — a real risk, since these are plain files with no versioned
  schema contract — the parser would need updating.
- **Historical depth**: OMIE's public file repository covers roughly the
  last 6 rolling years. Older data requires a manual request through
  OMIE's assistance portal and is out of scope for this application's
  automated ingestion.
- **Market-split precision**: this application stores the published
  marginal clearing price only. It does not model or store the underlying
  bid/supply/demand curves, technical-constraints adjustments (the
  "viable daily program," which can differ slightly from the raw market
  result), or cross-border capacity data.
- **Single data source**: only OMIE's public files are used. No ESIOS/REE
  API integration (would require a manually-requested API token, which was
  not available within this project's time constraints).
- **Not financial infrastructure**: this is an educational/demonstration
  project. It is not a source of truth for settlement, trading, or any
  financial decision-making.
- **Timezone dependency**: relies on the `Europe/Madrid` IANA timezone ID
  being resolvable on the host OS. Supported cross-platform (Windows,
  Linux, macOS) since .NET 6, so this should not be an issue on any
  reasonably current .NET 10 deployment target.

## AI usage transcripts

Complete, unedited AI conversation transcripts from the research and
implementation phases are in `ai-transcripts/`.
https://claude.ai/share/8e932b81-4d76-4e74-b130-2b0d57f4bbab

## Attribution

Day-Ahead auction data © OMIE (OMI-Polo Español, S.A.). This project is
independent and not affiliated with or endorsed by OMIE.
