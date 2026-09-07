# AGENTS.md

Guidance for AI agents working in this repo. Human-readable conventions and gotchas that aren't
obvious from the code alone.

## What this project is

Billing service for the Laraue.* ecosystem. It's the single source of truth for tariffs,
subscriptions and token balances shared across the other Laraue apps (currently `Laraue Boards`,
`Laraue Markdown Translator`). Other apps aren't expected to know about pricing, currencies or
token accounting themselves - they ask this service.

Exposed via one surface today:

- A public web API (`Laraue.Apps.Billing.WebApi`) that returns tariffs for a service in a given
  currency, for frontend pricing pages. See `TariffsController`/`TariffService`.

`Laraue.Apps.Billing.Internal.Contracts` additionally defines the *intended* service-to-service
contract other apps will call directly (`ISubscriptionService`/`ILaraueBoardsSubscriptionService`
for "does this user/org have an active subscription", `ITokenService` for reserving/committing/
cancelling token spend). These interfaces are designed and documented (see the conversation-style
comments above each interface in that project) but **not yet implemented** anywhere in this repo -
there's no controller or gRPC/internal endpoint serving them yet. Don't assume they're wired up.

## Domain model

- `Service` - a consuming app (`LaraueBoards`, `MarkdownTranslator`), identified by `ServiceId`.
- `Tariff` - a priced plan (`Price` in USD cents, `BillingPeriod` Month/Forever, `IncludedTokensCount`,
  `Type` Personal/Team, `IsActive`). Service-specific extra fields live on a joined
  `{Service}{Type}Tariff` row with a 1:1 FK to `Tariff` (`LaraueBoardsPersonalTariff`,
  `LaraueBoardsTeamTariff`, `MarkdownTranslatorPersonalTariff`) rather than on `Tariff` itself, so
  each service can have its own tariff shape without a wide shared table. There's no
  `MarkdownTranslatorTeamTariff` yet - Markdown Translator only sells personal plans, so
  `TariffService.GetTeamSubscriptionsAsync` returns an empty list for it instead of querying.
- `CurrencyRate` - one row per supported currency (`Code`, `Symbol`, `RateToUsd`, `RoundingStep`,
  `RoundingMode`). All prices are stored in USD cents on `Tariff`/`TokenPack` and converted to the
  requested currency on read (`TariffService.ConvertPrice`/`RoundPrice`/`FormatPrice`) - there's no
  per-currency price column anywhere. Adding a new supported currency is a `CurrencyRatesData` seed
  entry, not a schema change.
- `TokenPack` - a one-off token top-up purchasable outside a subscription (`Price`, `TokensCount`,
  expiration).
- `Subscription` - an active/cancelled paid plan tying a `PaidEntityId` (user or org id) to a
  `Tariff`, with an `OwnerId` (who's actually paying, which can differ from who's covered - e.g. an
  org admin paying for a team plan).
- `BalancePurchasedToken`/`BalanceSubscriptionToken` - **materialized** current balances (not
  computed on the fly from the transaction log), keyed by `PaidEntityId`/`SubscriptionId`
  respectively. Keep them in sync with `TokenTransaction` writes rather than trusting them to be
  derivable after the fact.
- `TokenTransaction` - the append-only ledger of token spend (`Status`
  Started/Canceled/Confirmed, `Reason` TariffGrant/DailyGrant/Purchase/Expiry), linked to which
  purchased pack(s) and/or subscription pack it drew from via
  `TokenTransactionPurchasedTokenPack`/`TokenTransactionSubscriptionTokenPack`.

All reference data (`Service`, `Tariff` + its per-service joins, `TokenPack`, `CurrencyRate`) is
seeded via EF Core `HasData` in `DatabaseContext.OnModelCreating`, sourced from the static classes
in `DataAccess/Data/*Data.cs` - there's no admin UI or seeding script; changing a tariff or adding a
currency means editing the relevant `*Data.cs` class and adding a migration.

## Pricing/currency conversion

`TariffService.ConvertPrice` converts a USD-cent price to the target currency
(`price / 100 / RateToUsd`) and rounds it via `RoundPrice` using the currency's `RoundingStep`
and `RoundingMode` (`Nearest`/`Up`/`Down`) - e.g. RUB rounds up to the nearest whole ruble, USD
rounds to the nearest cent. `FormatPrice` reuses `ConvertPrice` and appends the currency symbol,
using the number of decimal places implied by `RoundingStep` (so a `RoundingStep` of `1M` formats
with zero decimals). Don't hand-round prices elsewhere - go through these so a currency's rounding
policy stays defined in one place (`CurrencyRatesData`).

## Project layout

Solution: `Laraue.Apps.Billing.sln`

- `src/Laraue.Apps.Billing.DataAccess` - EF Core `DatabaseContext`, entities, migrations, and the
  static seed data (`Data/*.cs`).
- `src/Laraue.Apps.Billing.Internal.Contracts` - host-agnostic request/response/interface shapes
  for the service-to-service contract other apps will consume (see "What this project is" above).
  No implementation, no DB/ASP.NET dependencies - kept minimal so it could be shared as a package.
- `src/Laraue.Apps.Billing.WebApiServices` - business logic for the public web API (`TariffService`),
  plus `Resources/Errors.resx` for user-facing error text.
- `src/Laraue.Apps.Billing.WebApi` - ASP.NET host: `Program.cs`, `WebApplicationBuilderExtensions`
  (DI wiring split into `AddDatabaseServices`/`AddApplicationServices`), controllers.
- `tests/Laraue.Apps.Billing.IntegrationTests` - the only test project, structured the same way as
  `Laraue.Apps.Boards`'s integration tests (see "Testing" below).

## User-facing text

Don't put string literals directly in `throw new SomeException("...")` calls. Use
`Resources/Errors.resx` (+ hand-maintained `Errors.Designer.cs` - see the note below) and
`string.Format(...)` for placeholders, same convention as `Laraue.Apps.Boards`.

**`Errors.Designer.cs` isn't auto-regenerated by `dotnet build`** on this machine - it's a
Visual-Studio-only single-file-generator step. After adding a `<data>` entry to `Errors.resx`, also
hand-add the matching `internal static string Foo { get { return
ResourceManager.GetString("Foo", resourceCulture); } }` property to `Errors.Designer.cs`, mirroring
an existing entry's shape.

## Error handling

`TariffService` throws `Laraue.Core.Exceptions.Web.BadRequestException` for both an unknown
`ServiceId` and an unknown currency code - both are client input errors (400), not server errors.
`ExceptionHandleMiddleware` (from the `Laraue.Core.Exceptions` package, registered in
`AddApplicationServices` and added via `app.UseMiddleware<ExceptionHandleMiddleware>()` in
`Program.cs`) catches these and any other `HttpException` and renders them as a JSON
`{ message, errors }` body. Note `UseMiddleware<ExceptionHandleMiddleware>()` is called *after*
`app.MapControllers()` in `Program.cs` - this looks backwards but is harmless: ASP.NET Core's
minimal-hosting model inserts endpoint execution as the terminal step of the pipeline regardless of
where `Map*` is called, so the exception middleware still wraps every request. Confirmed by the
integration tests' bad-request cases actually getting a 400 back. Don't "fix" the ordering without
checking the tests still pass either way - it doesn't need fixing.

## EF Core

Follow `Laraue.Apps.Boards`'s conventions: default to plain EF Core LINQ, project straight to the
response shape with `.Select(...)` instead of `Include`-ing full entity graphs (see
`TariffService.GetLaraueBoardsPersonalSubscriptionsAsync` etc. for the pattern - anonymous
projection then a second `.Select` into the polymorphic response record).

## Testing

`tests/Laraue.Apps.Billing.IntegrationTests` mirrors `Laraue.Apps.Boards.IntegrationTests`'s
infrastructure and conventions:

- `Infrastructure/WebApiTestHost.cs` - a `WebApplicationFactory<Program>` with a
  `Controller<TController>()` helper returning a `Laraue.Core.Testing.Http.Proxy<TController>`
  (see that package's readme in the `Laraue.Core` repo for how the proxy resolves routes/binding
  from a strongly-typed expression against the controller). Test classes take `WebApiTestHost` via
  constructor injection and implement `IClassFixture<WebApiTestHost>`.
- Migrations run automatically on host startup (`Program.Main`'s own
  `db.Database.MigrateAsync()` call executes as part of `WebApplicationFactory<Program>` building
  the test server) - no separate migration step needed in tests.
- All of today's reference data (tariffs, currencies, services) is static seed data from `HasData`,
  so the current tests don't need a `CreateTestScope()`/`CleanDatabase()`-style helper the way
  Boards' tests do - there's no per-test user/org data to create or clean up yet. Add one (mirroring
  `Laraue.Apps.Boards.IntegrationTests.Infrastructure.DbExtensions.CleanDatabase`) once tests start
  writing rows (subscriptions, token transactions, etc.) that need isolating between tests.
  Tests run against their own database, not the dev one - see `tests/.../appsettings.json`'s
  `billing_tests` connection string vs. `src/Laraue.Apps.Billing.WebApi/appsettings.json`'s `billing`.
- **Naming convention**: `{Handler}_Should{ExpectedBehavior}_When{Condition}`, matching Boards.
  Prefer a separate `[Fact]`/`[Theory]` per case over one test covering several scenarios.

## Database safety

- Migrations are applied automatically on startup (`WebApi` and the integration test host both run
  `MigrateAsync()`/`Migrate()` at boot) - there's normally no need to run `dotnet ef database
  update` by hand.
- **Never run `dotnet ef database drop`.** `--startup-project` determines which
  `appsettings.json` connection string is used, and it does *not* necessarily point at the test
  database - it's easy to accidentally drop the dev DB (`billing`) by mistake.
- Before any destructive DB operation, confirm which database (dev `billing` vs. test
  `billing_tests`) the command will actually target, and ask the user first if there's any ambiguity.

## Build-lock protocol

`dotnet build` can fail with `MSB3026`/`MSB3027` file-lock errors if `Laraue.Apps.Billing.WebApi` (or
another host process) is already running locally and holding the output DLLs open. Don't kill the
process yourself - ask the user to stop it, then retry the build once they confirm.

## Task flow

- Create branch with pattern `feature/task-number-task-description`, e.g.
  `feature/BIL-12-add-token-pack-purchase`, for new task, matching `Laraue.Apps.Boards`'s convention.
