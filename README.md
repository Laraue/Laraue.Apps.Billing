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

Published to NuGet.org as `Laraue.Apps.Billing.Internal.Contracts` on every push, from any branch
(see `.github/workflows/nuget-publish.yml`) - `main` publishes the real version, any other branch
publishes a `-alpha.<run number>` prerelease so another service can integrate against an
in-progress contract change before it merges.

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

## Payments

A service starts a payment over gRPC (`payment.proto`), naming the tariff or token pack; Billing prices it
itself, stores a pending `Payment` and returns the payment provider's address. The provider calls Billing
back on public addresses, `/api/payments/{provider}/notify` (the server-to-server result, which is the only
proof of payment) and `/success` / `/fail` (where the customer returns). On a success or fail return Billing
finds the payment and redirects the customer to the pages configured for its service in
`Payments:Redirects:Services:{ServiceId}`.

Tariffs are offered, and checkouts routed, only in currencies a registered provider can charge in. How the
callbacks are handled, why there is one generic callback controller and not one per provider, and what adding
a provider or a product takes: see [AGENTS.md](AGENTS.md#payments).
