# Laraue.Apps.Billing

Billing service shared across the Laraue.* apps (currently `Laraue Boards` and
`Laraue Markdown Translator`): tariffs, currency-aware pricing, subscriptions and token balances
live here instead of being duplicated per app.

## App structure

### Laraue.Apps.Billing.DataAccess
EF Core `DatabaseContext`, entity models, migrations, and the static reference data (tariffs,
currency rates, services, token packs) seeded via `HasData`.

### Laraue.Apps.Billing.Internal.Contracts
The gRPC contract other apps call directly: `subscription.proto`
(`SubscriptionService.GetActivePersonalSubscription`/`GetActiveOrganizationSubscription`) plus its
generated client/server stubs, served by `InternalApiHost`/`InternalApiServices`. `ITokenService`
is still a plain C# sketch, not yet a proto contract or implemented anywhere - see
[AGENTS.md](AGENTS.md).

### Laraue.Apps.Billing.Services
Business logic shared across hosts (not tied to any one of them), e.g. `TariffService` (currency
conversion, rounding, and formatting for a service's tariffs) and `SubscriptionService` (active
subscription lookup, keyed by service + user/organization).

### Laraue.Apps.Billing.WebApiHost
The public ASP.NET web host: `Program.cs`, DI wiring, and its own `Controllers/TariffsController`.

### Laraue.Apps.Billing.InternalApiServices
The gRPC-facing implementation of `Internal.Contracts` (`SubscriptionGrpcService`), mapping between
the wire contract and `Laraue.Apps.Billing.Services`. Kept separate from `InternalApiHost` so the
host project stays bootstrap-only.

### Laraue.Apps.Billing.InternalApiHost
The internal gRPC host: `Program.cs` only (Kestrel, DI, OpenTelemetry wiring, migrations on
startup) - no service implementations of its own. Listens on plain HTTP/2 (h2c, no TLS), since
this is trusted service-to-service traffic, not public-facing.

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
