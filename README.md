# Laraue.Apps.Billing

Billing service shared across the Laraue.* apps (currently `Laraue Boards` and
`Laraue Markdown Translator`): tariffs, currency-aware pricing, subscriptions and token balances
live here instead of being duplicated per app.

## App structure

### Laraue.Apps.Billing.DataAccess
EF Core `DatabaseContext`, entity models, migrations, and the static reference data (tariffs,
currency rates, services, token packs) seeded via `HasData`.

### Laraue.Apps.Billing.Internal.Contracts
Request/response/interface shapes for the service-to-service contract other apps will call
(`ISubscriptionService`, `ITokenService`). Not implemented yet - see [AGENTS.md](AGENTS.md).

### Laraue.Apps.Billing.WebApiServices
Business logic for the public web API, e.g. `TariffService` (currency conversion, rounding, and
formatting for a service's tariffs).

### Laraue.Apps.Billing.WebApiHost
The ASP.NET host: `Program.cs`, DI wiring, controllers.

## Local run

1. Have Postgres running locally and reachable with the credentials in
   `src/Laraue.Apps.Billing.WebApiHost/appsettings.json`'s `ConnectionStrings:Postgre` (defaults to
   `User ID=postgres;Password=postgres;Host=localhost;Port=5432`, database `billing`).
2. Run the API:
   ```
   dotnet run --project src/Laraue.Apps.Billing.WebApiHost
   ```
   Migrations apply automatically on startup - no separate `dotnet ef database update` step.
3. The API listens on `http://localhost:5262` by default (see `Properties/launchSettings.json`).
   In `Development`, Scalar API docs are available under `/scalar`.

### Example request
```
GET http://localhost:5262/api/tariffs?serviceId=LaraueBoards&currencyCode=USD
```
`serviceId` is `LaraueBoards` or `MarkdownTranslator`; `currencyCode` is `USD` or `RUB` today (see
`CurrencyRatesData` for the full seeded list). Both are validated - an unknown service or currency
code returns `400 Bad Request`.

## Running tests

`tests/Laraue.Apps.Billing.IntegrationTests` needs its own Postgres database, separate from the dev
one - see that project's `appsettings.json` (`billing_tests`). Migrations run automatically when the
test host starts, same as the real API.
```
dotnet test tests/Laraue.Apps.Billing.IntegrationTests
```

## Pricing model

Prices are stored once, in USD cents, on `Tariff`/`TokenPack`. Every other currency is a
`CurrencyRate` row (`RateToUsd`, `RoundingStep`, `RoundingMode`) used to convert and round the USD
price on read - there's no per-currency price column to keep in sync. See
[AGENTS.md](AGENTS.md#pricingcurrency-conversion) for the conversion/rounding details.
