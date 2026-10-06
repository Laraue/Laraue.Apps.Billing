# AGENTS.md

Guidance for AI agents working in this repo. Human-readable conventions and gotchas that aren't
obvious from the code alone.

## What this project is

Billing service for the Laraue.* ecosystem. It's the single source of truth for tariffs,
subscriptions and token balances shared across the other Laraue apps (currently `Laraue Boards`,
`Laraue Markdown Translator`). Other apps aren't expected to know about pricing, currencies or
token accounting themselves - they ask this service.

Exposed via one surface today:

- A public web API (`Laraue.Apps.Billing.WebApiHost`) that returns tariffs for a service in a given
  currency, for frontend pricing pages. See `TariffsController`/`TariffService`.

`Laraue.Apps.Billing.Internal.Contracts` additionally holds the gRPC contract other apps will call
directly for "does this user/org have an active subscription": `Protos/subscription.proto`
(`SubscriptionService.GetActivePersonalSubscription`/`GetActiveOrganizationSubscription`, response
polymorphic per calling service via a protobuf `oneof` - see the message comments in the `.proto`
itself), built with `GrpcServices="Both"` so it ships both the client stub (for callers like
`Laraue.Apps.Boards`) and the server base class from one package. Uses
[`Laraue.Grpc`](https://github.com/Laraue/Laraue.Grpc) (`Laraue.Grpc.Server`/`.Client`/
`.OpenTelemetry`) for interceptor-based tracing/metrics on both sides - see that repo's readme for
how it works. Implemented by `Laraue.Apps.Billing.InternalApiHost`/`.InternalApiServices` (see
"Project layout" below). `ITokenService` (token reserve/commit/cancel) is still a plain C# interface
sketch, not a proto contract, and not implemented anywhere yet.

`Internal.Contracts` is published to NuGet.org as `Laraue.Apps.Billing.Internal.Contracts` so other
apps (`Laraue.Apps.Boards`) can reference it - see "NuGet publishing" below.

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
- **Free allowances** - a free (`IsFree`, `BillingPeriod.Forever`) tariff never renews, so its tokens
  come from a recurring top-up of `BalanceSubscriptionToken.FreeTokensCount` that *resets* (never
  accumulates) it, applied lazily by `SubscriptionService.GetOrCreateActive*SubscriptionIdAsync`:
  daily for Markdown Translator (`IncludedDailyFreeTokensCount`), monthly for the Boards Free
  tariffs (`Tariff.IncludedTokensCount`, 25,000 per UTC month - zero means no monthly allowance).
  Both write to the same `FreeTokensCount` bucket, so a tariff must have one or the other, not both.
  A new free subscription starts with an empty `SubscriptionTokensCount` (no one-time grant).
  The monthly top-up writes a `TariffGrant` ledger row (the net change), because
  `TokenTransactionReason` is mirrored in the published gRPC contract and a new value would need a
  contracts release.
- `Payment` - a customer's attempt to pay for a tariff or a token pack (`Kind`), in minor units of
  `CurrencyCode`, with a `Status` (Pending/Paid/Failed/Canceled). Provider-agnostic on purpose: the
  provider is a string code (`Provider`), and its own references live only in `ProviderPaymentId`
  (unique per provider, opaque) and `ProviderData` (jsonb, read/written only by that provider's
  code). Never add a column for one provider's quirk (e.g. an integer invoice number) - keep it in
  those two fields, so replacing a provider needs no schema change.
- `TokenTransaction` - the append-only ledger of token spend (`Status`
  Started/Canceled/Confirmed, `Reason` TariffGrant/DailyGrant/Purchase/Expiry), linked to which
  purchased pack(s) and/or subscription pack it drew from via
  `TokenTransactionPurchasedTokenPack`/`TokenTransactionSubscriptionTokenPack`.

All reference data (`Service`, `Tariff` + its per-service joins, `TokenPack`, `CurrencyRate`) is
seeded via EF Core `HasData` in `DatabaseContext.OnModelCreating`, sourced from the static classes
in `DataAccess/Data/*Data.cs` - there's no admin UI or seeding script; changing a tariff or adding a
currency means editing the relevant `*Data.cs` class and adding a migration.

## Payments

Provider-agnostic, in `Services/Payments`. Everything provider-specific sits behind `IPaymentProvider`
(checkout link, notification verification/parsing, the acknowledgement body); a provider is its own
project that registers an `IPaymentProvider`, and `Payments:DefaultProvider` picks the one new
checkouts use. `ICorePaymentService.CreateAsync` prices the item from our own tariffs/packs (never from
the caller), stores a Pending `Payment` and returns the provider's link. `HandleNotificationAsync` is
idempotent: it locks on the payment id, checks amount/currency against the stored payment, and
`IPaymentFulfillment` (subscription activation/extension, token pack credit, ledger row) runs in the
same transaction as the status change. Don't branch on a provider code anywhere outside the provider.

`WebApiHost` exposes the providers' public addresses in `PaymentsController`, generic over the provider in the
route: `/api/payments/{provider}/notify` (GET or POST, the answer body is the provider's acknowledgement),
`/success` and `/fail` (redirect the customer to the pages of the service the payment belongs to,
`Payments:Redirects:Services:{ServiceId}`, or to the global `Payments:Redirects:SuccessUrl`/`FailUrl` when the
payment cannot be identified - the success page proves nothing, only a notification pays). Providers are registered in `WebApiServices`'
`AddWebApiServices(configuration)`; options are validated lazily, on first use. Configure
`Payments:Robokassa` (`MerchantLogin`, passwords, `IsTest: false` in production) via secrets; the
Robokassa shop settings must point ResultURL/SuccessURL/FailURL to these addresses.

Other services start a payment over gRPC (`payment.proto`: `CreatePersonalCheckout`/`CreateOrganizationCheckout`,
implemented by `InternalApiServices.PaymentGrpcService` over `ICorePaymentService.CreateAsync`): they name the
item and get back the provider's URL, never an amount or a provider. `AddPaymentServices(configuration)`
(in `Services`) registers the provider-agnostic part and is shared by both hosts; each host adds the provider it
uses next to it.

**Log generously at Information** in everything payment-related (the `WebApiHost` runs at Information in
production): every decision and state change with the ids needed to follow one payment end to end
(`PaymentId`, provider payment id, paid entity, amounts, statuses), plus a Warning for every refusal.
Use `ILogger<T>` and structured templates. Never log secrets: a provider logs notification parameter
*names*, not values, since they carry its signature, and never its passwords or keys.

## Adding a product

A new product is a new `ServiceId`. Everything provider- and currency-related is already generic (any
service is priced and paid through the same code); what is per product is below. Do all of it - the
`ServiceOnboardingTests` theory runs over every `ServiceId` and fails for a product that has no paid
tariffs, a tariff that `TariffService` cannot map, or tariffs `CorePaymentService` does not recognise.

1. `DataAccess`: the `ServiceId` value (`Entities/Service.cs`), its row in `Data/ServicesData.cs`, a
   `{Service}PersonalTariff` (and `{Service}TeamTariff` when it sells team plans) entity with its
   `DbSet`/mapping in `DatabaseContext`, the tariffs in `Data/{Service}TariffsData.cs` (at least one
   paid one) and a migration. Free allowances need a `BalanceSubscriptionToken` rule, see "Free
   allowances" above.
2. Reading tariffs: the `Core{Service}...Tariff` records and their loading in `CoreTariffService`, and
   the mapping to the public `{Service}...Subscription` types in `WebApiServices/TariffService`.
3. Paying: add the service to the item check in `CorePaymentService.GetTariffItemAsync` (the switch
   on `ServiceId`, personal and team tables). A service missing there can never be bought.
4. gRPC: the service in `subscription.proto` (its `ServiceId` value, the `oneof` payload message),
   `GrpcParsing.ReadDomainServiceId`, `SubscriptionGrpcService` and `SubscriptionService`
   (the active subscription of the service). Changing the `.proto` needs a NuGet release of
   `Laraue.Apps.Billing.Internal.Contracts` - see "NuGet publishing".
5. Config: `Payments:Redirects:Services:{ServiceId}` with that product's `SuccessUrl`/`FailUrl`. The
   provider sends every customer back to the one `/api/payments/{provider}/success|fail` address;
   Billing finds the payment from the returned parameters and redirects to its service's pages. A
   service without an entry falls back to the global `SuccessUrl`/`FailUrl` - fine only for the first
   product. The provider's shop settings keep pointing at Billing, never at a product.
6. The product's own backend calls Billing with the `x-laraue-service-id` header and its own tariff
   ids; the checkout currency is one Billing offers (`GET /api/tariffs` returns tariffs only in
   currencies a registered provider charges in).
7. Not per product yet: the default provider and the provider credentials (one Robokassa shop). A
   product that needs its own shop or provider needs per-service provider settings first.

## Pricing/currency conversion

`PriceCalculator.ConvertPrice` (in `Services`, a static utility - no DB/DI dependency) converts a
USD-cent price to the target currency (`price / 100 / RateToUsd`) and rounds it via the private
`RoundPrice` using the currency's `RoundingStep` and `RoundingMode` (`Nearest`/`Up`/`Down`) - e.g.
RUB rounds up to the nearest whole ruble, USD rounds to the nearest cent. `FormatPrice` reuses
`ConvertPrice` and appends the currency symbol, using the number of decimal places implied by
`RoundingStep` (so a `RoundingStep` of `1M` formats with zero decimals). Both take the individual
`CurrencyRate` properties they need (`rateToUsd`, `roundingStep`, `roundingMode`, `symbol`) rather
than the whole `CurrencyRate` model. `CoreTariffService` (also in `Services`) is the only caller -
it applies `PriceCalculator` while building each `CoreTariff` row, so `Price`/`FormattedPrice`/
`BillingDuration` arrive already computed. A host's `Host{Services}` layer never calls
`PriceCalculator` itself and never re-derives a price or billing duration - see "Project layout".
Don't hand-round prices elsewhere - go through `PriceCalculator` so a currency's rounding policy
stays defined in one place (`CurrencyRatesData`).

## Project layout

Solution: `Laraue.Apps.Billing.sln`

- `src/Laraue.Apps.Billing.DataAccess` - EF Core `DatabaseContext`, entities, migrations, and the
  static seed data (`Data/*.cs`).
- `src/Laraue.Apps.Billing.Internal.Contracts` - the `.proto` service-to-service contract other apps
  will consume plus its generated stubs (see "What this project is" above). No implementation, no
  DB/ASP.NET dependencies - kept minimal so it could be shared as a package.
- `src/Laraue.Apps.Billing.Payments.Robokassa` - the Robokassa `IPaymentProvider` (checkout link,
  signatures, result notification parsing) and `AddRobokassaPaymentProvider(configuration)`, which binds
  `Payments:Robokassa` (`RobokassaOptions`). References only `Services`; a host registers it. We never send
  an `InvId`: Robokassa assigns it, it comes back in the result notification and is stored as
  `Payment.ProviderPaymentId`; our payment id travels as the custom `Shp_paymentId` parameter, which
  Robokassa echoes and signs. Another provider = another project like this one, nothing else changes.
- `src/Laraue.Apps.Billing.Services` - **core** business logic shared across hosts, not tied to any
  one of them (`CoreTariffService`, `SubscriptionService`, ...), plus `Resources/Errors.resx` for
  user-facing error text. "Core" here means: does all the actual computation, but no host-facing
  response shape. `CoreTariffService` validates the currency code and, for each per-service tariff
  row, computes `Price`/`FormattedPrice` (via `PriceCalculator`) and `BillingDuration` (null when
  `BillingPeriod` is `Forever`, else `1`) up front, returning fully-priced
  `CoreLaraueBoardsPersonalTariff`/`CoreMarkdownTranslatorPersonalTariff`/`CoreLaraueBoardsTeamTariff`
  rows (all deriving from `CoreTariff`), already ordered by price. It does not know about
  `GetServiceTariffsResponse`, the `PersonalSubscription`/`TeamSubscription` JSON-polymorphic
  hierarchy, or any other per-host DTO. A host's `Host{Services}` layer only maps a `CoreTariff` row
  1:1 onto its own DTO fields (switch on the concrete `CoreTariff` subtype to pick the right DTO
  type) - it must not recompute a price, a billing duration, or otherwise duplicate `Services`
  logic; if a mapping needs a derived value, that derivation belongs in `Services`, not repeated (or
  worse, drifting) across every host.
- `src/Laraue.Apps.Billing.WebApiHost` - the public ASP.NET host: `Program.cs`,
  `WebApplicationBuilderExtensions` (DI wiring split into `AddDatabaseServices`/
  `AddApplicationServices`), and its (thin) `Controllers/TariffsController`. Controllers stay in the
  host - unlike the gRPC side below, there's no separate project for them.
- `src/Laraue.Apps.Billing.WebApiServices` - `WebApiHost`'s own DI composition *and DTO mapping*
  layer over `Services`. Owns `ITariffService`/`TariffService`, `GetServiceTariffsRequest`/
  `GetServiceTariffsResponse` and the `Subscription`/`PersonalSubscription`/`TeamSubscription`
  JSON-polymorphic response hierarchy - `TariffService` here calls `ICoreTariffService` for
  already-priced `CoreTariff` rows and copies each one's fields onto the matching `*Subscription`
  record (switch on the `CoreTariff` subtype); it does no price/billing-duration math of its own.
  `ServiceCollectionExtensions.AddWebApiServices()` registers both `ICoreTariffService`/
  `CoreTariffService` and `ITariffService`/`TariffService`. `WebApiHost` doesn't reference `Services`
  directly - it only references `WebApiServices`, which owns the `Services` reference;
  `TariffsController` (in the host) depends only on `ITariffService` from `WebApiServices`, not on
  anything from `Services`.
- `src/Laraue.Apps.Billing.InternalApiServices` - the gRPC-facing implementation of
  `Internal.Contracts` (`SubscriptionGrpcService`, mapping wire types <-> `Laraue.Apps.Billing.Services`
  DTOs) plus its own DI composition (`ServiceCollectionExtensions.AddInternalApiServices()` registers
  `ISubscriptionService`/`SubscriptionService`). Kept out of `InternalApiHost` on purpose - see the
  next bullet.
- `src/Laraue.Apps.Billing.InternalApiHost` - the internal gRPC host: `Program.cs` only
  (Kestrel/DI/OpenTelemetry wiring, migrations on startup). No gRPC service implementations of its
  own - those belong in `InternalApiServices` instead. This is an intentional asymmetry with
  `WebApiHost` above (which does keep its controllers directly), not an inconsistency to "fix" by
  moving controllers out too - that was tried and reverted.
  Listens on **two separate ports** (`Kestrel:GrpcPort`/`:HealthPort` in config, 5263/5264 by
  default) - one HTTP/2-only for gRPC, one HTTP/1.1-only for `/_health`/`/_metrics`. This isn't a
  stylistic choice: Kestrel cannot multiplex HTTP/1.1 and cleartext HTTP/2 (h2c) on the *same*
  endpoint without TLS (no ALPN to pick per-connection) - a single endpoint declared
  `HttpProtocols.Http1AndHttp2` over plaintext silently downgrades to HTTP/1.1-only, which breaks
  every gRPC call with a client-side `HTTP_1_1_REQUIRED` error. This host originally used exactly
  that single-endpoint config (with a comment claiming it was fine) - confirmed broken by actually
  running the host and calling it over a real socket, not just via
  `WebApplicationFactory`'s in-memory `TestServer` (which bypasses Kestrel's real listen config
  entirely and never exercises this - this repo's own integration tests didn't catch it either). If
  you ever "simplify" this back to one shared `Http1AndHttp2` endpoint, you will silently
  reintroduce this bug.

Each host follows a `Host -> Host{Services} -> Services` layering: the shared `Services` project
stays host-agnostic (core domain logic + raw/`Core*` DTOs, no per-host response shape) and is never
referenced directly by a host project (`WebApiHost`, `InternalApiHost`) - only by that host's own
`*Services` project (`WebApiServices`, `InternalApiServices`), which exposes an `Add{Host}Services()`
`IServiceCollection` extension the host calls instead of registering `Services` types itself, and
which owns projecting core data into that host's own DTOs. When adding a new host, give it its own
`Host{Services}` project (with its own DTOs/projection logic) rather than referencing `Services` -
or its DTO shapes - from the host directly, and rather than adding a host-specific DTO or projection
onto a `Core*` type in `Services`.
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

`CoreTariffService` throws `Laraue.Core.Exceptions.Web.BadRequestException` for both an unknown
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

Follow `Laraue.Apps.Boards`'s conventions: default to plain EF Core LINQ, project straight to a
shape with `.Select(...)` instead of `Include`-ing full entity graphs (see
`CoreTariffService.GetLaraueBoardsPersonalTariffsAsync` etc. for the pattern - an anonymous
projection of just the columns needed, `ToListAsync`, then a second in-memory `.Select(...)` into
the `Core*` record that also applies `PriceCalculator` to the raw `Price` column; the second step
has to happen client-side since `PriceCalculator`'s rounding isn't SQL-translatable). A host's
`*Services` project then maps those already-priced `Core*` records 1:1 onto its own response shape
(see `WebApiServices.TariffService`) - EF Core itself is never queried outside `Services`.

**Transactions belong to the host.** A core service in `Services` never opens a transaction: one that
writes several rows or takes `PgAdvisoryXactLock` starts with `context.Database.EnsureTransactionStarted()`
and leaves the lifecycle to its caller. The host (gRPC service, `WebApiServices`, a job) runs the call
through `context.Database.InTransactionAsync(...)`, which commits on success and rolls back on an
exception. Tests that call a core service directly do the same through `InTransaction(...)` of
`BillingIntegrationTest`.

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
  `billing_tests` connection string vs. `src/Laraue.Apps.Billing.WebApiHost/appsettings.json`'s `billing`.
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

## NuGet publishing

`.github/workflows/nuget-publish.yml` packs and pushes every packable project (`IsPackable=true` -
currently only `Laraue.Apps.Billing.Internal.Contracts`) to NuGet.org via trusted publishing (OIDC,
no stored API key - see `Laraue.Grpc`'s readme for how that mechanism works), separately from
`dotnet.yml`'s build/test/deploy pipeline. It runs on **every branch push**, not just `main`:

- On `main`, it publishes the base `<Version>` from `Directory.Build.props` as-is.
- On any other branch, it publishes `<Version>-alpha.<run number>` (e.g. `0.0.1-alpha.42`) instead -
  a SemVer2 prerelease, so `dotnet add package`/restore never picks it up unless a consumer
  explicitly asks for a prerelease or pins that exact version. This is the intended way to work on a
  new/changed contract on a branch and let another service (e.g. `Laraue.Apps.Boards`) start
  integrating against it immediately, before the branch merges.

Needs a `NUGET_USER` repo secret (the nuget.org profile name, not email) and a trusted publishing
policy configured on nuget.org for this repo/workflow file - see `Laraue.Grpc`'s readme for the
exact nuget.org setup steps, same mechanism.

Adding a new packable contract project later needs no workflow changes - just add
`<IsPackable>true</IsPackable>` to that project (see `Internal.Contracts`'s `.csproj`), the
solution-wide `dotnet pack` in this workflow picks it up automatically.

## Build-lock protocol

`dotnet build` can fail with `MSB3026`/`MSB3027` file-lock errors if `Laraue.Apps.Billing.WebApiHost` (or
another host process) is already running locally and holding the output DLLs open. Don't kill the
process yourself - ask the user to stop it, then retry the build once they confirm.

## Task flow

- Create branch with pattern `feature/task-number-task-description`, e.g.
  `feature/BIL-12-add-token-pack-purchase`, for new task, matching `Laraue.Apps.Boards`'s convention.
