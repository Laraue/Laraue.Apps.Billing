using Laraue.Apps.Billing.DataAccess.Data;
using Laraue.Apps.Billing.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.DataAccess;

public class DatabaseContext : DbContext
{
    public DatabaseContext(DbContextOptions options) 
        : base(options)
    {
    }
    
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
    }
}