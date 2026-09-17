using Laraue.Apps.Billing.DataAccess.Data;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Core.Extensions.Hosting.EfCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.DataAccess;

public class DatabaseContext : DbContext, IJobsDbContext
{
    public DatabaseContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>
    /// Backing store for <c>Laraue.Core.Extensions.Hosting</c>'s background job runners (e.g.
    /// <c>WorkerHost</c>'s stale-reservation reconciliation job) - tracks each job's last/next
    /// execution time so it survives a process restart.
    /// </summary>
    public required DbSet<JobStateEntity> JobStates { get; set; }

    #region Tariffs

    public required DbSet<Tariff> Tariffs { get; set; }
    public required DbSet<LaraueBoardsPersonalTariff> LaraueBoardsPersonalTariffs { get; set; }
    public required DbSet<LaraueBoardsTeamTariff> LaraueBoardsTeamTariffs { get; set; }
    public required DbSet<MarkdownTranslatorPersonalTariff> MarkdownTranslatorPersonalTariffs { get; set; }
    public required DbSet<TokenPack> TokenPacks { get; set; }
    public required DbSet<CurrencyRate> CurrencyRates { get; set; }
    public required DbSet<Service> Services { get; set; }

    #endregion

    #region Balance & Transactions

    public required DbSet<Subscription> Subscriptions { get; set; }
    public required DbSet<BalancePurchasedToken> BalancePurchasedTokens { get; set; }
    public required DbSet<BalanceSubscriptionToken> BalanceSubscriptionTokens { get; set; }
    public required DbSet<PurchasedTokenPack> PurchasedTokenPacks { get; set; }
    public required DbSet<TokenTransaction> TokenTransactions { get; set; }
    public required DbSet<TokenTransactionPurchasedTokenPack> TokenTransactionPurchasedTokenPacks { get; set; }
    public required DbSet<TokenTransactionSubscriptionTokenPack> TokenTransactionSubscriptionTokenPacks { get; set; }

    #endregion

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // JobStateEntity ships from Laraue.Core.Extensions.Hosting.EfCore with no key convention
        // of its own - JobName is documented as the "unique job name" on the base JobState record,
        // so it's the natural primary key.
        modelBuilder.Entity<JobStateEntity>()
            .HasKey(x => x.JobName);

        modelBuilder.Entity<Service>()
            .HasData(ServicesData.Services);

        modelBuilder.Entity<Tariff>()
            .HasData(
                LaraueBoardsTariffsData.PersonalTariffs.Select(x => x.Tariff)
                    .Concat(LaraueBoardsTariffsData.TeamTariffs.Select(x => x.Tariff))
                    .Concat(MarkdownTranslatorTariffsData.PersonalTariffs.Select(x => x.Tariff)));

        modelBuilder.Entity<LaraueBoardsPersonalTariff>(builder =>
        {
            builder.HasData(
                LaraueBoardsTariffsData.PersonalTariffs
                    .Select(x => x.Entity));

            builder
                .HasOne(x => x.Tariff)
                .WithOne()
                .HasForeignKey<LaraueBoardsPersonalTariff>(x => x.Id);
        });

        modelBuilder.Entity<LaraueBoardsTeamTariff>(builder =>
        {
            builder.HasData(
                LaraueBoardsTariffsData.TeamTariffs
                    .Select(x => x.Entity));

            builder
                .HasOne(x => x.Tariff)
                .WithOne()
                .HasForeignKey<LaraueBoardsTeamTariff>(x => x.Id);
        });

        modelBuilder.Entity<MarkdownTranslatorPersonalTariff>(builder =>
        {
            builder.HasData(
                MarkdownTranslatorTariffsData.PersonalTariffs
                    .Select(x => x.Entity));
            
            builder
                .HasOne(x => x.Tariff)
                .WithOne()
                .HasForeignKey<MarkdownTranslatorPersonalTariff>(x => x.Id);
        });

        modelBuilder.Entity<TokenPack>()
            .HasData(TokenPacksData.Packs);
        
        modelBuilder.Entity<CurrencyRate>()
            .HasData(CurrencyRatesData.CurrencyRates);

        modelBuilder.Entity<BalancePurchasedToken>(builder =>
        {
            builder.HasKey(x => x.PaidEntityId);
        });

        modelBuilder.Entity<BalanceSubscriptionToken>(builder =>
        {
            builder.HasKey(x => x.SubscriptionId);
        });

        // Every reserve/commit/cancel call and gRPC subscription lookup filters on exactly this
        // pair (GetActiveSubscriptionsQuery) - without it, only the individual service_id/tariff_id
        // FK indexes exist, forcing a sequential scan on this hot path as the table grows.
        modelBuilder.Entity<Subscription>()
            .HasIndex(x => new { x.ServiceId, x.PaidEntityId });

        // TryReserveTokensAsync filters PaidEntityId == x && ExpiredAt > now, then orders by
        // ExpiredAt, on every reservation - this table had no index on PaidEntityId at all.
        modelBuilder.Entity<PurchasedTokenPack>()
            .HasIndex(x => new { x.PaidEntityId, x.ExpiredAt });

        // GetTokenTransactionsAsync filters PaidEntityId == x then orders by CreatedAt desc (Id
        // desc as tie-break) for every page - without this, only the FK index on
        // SubscriptionTokensSpentId exists, forcing a sequential scan + sort on this table as it
        // grows (append-only, so it only ever gets bigger). Descending on both columns to match
        // the query's own ordering exactly, rather than relying on Postgres to reverse-scan an
        // ascending index for both keys at once.
        modelBuilder.Entity<TokenTransaction>()
            .HasIndex(x => new { x.PaidEntityId, x.CreatedAt, x.Id })
            .IsDescending(false, true, true);
    }
}