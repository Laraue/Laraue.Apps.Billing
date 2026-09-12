using Laraue.Apps.Billing.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.IntegrationTests.Infrastructure;

public static class DbExtensions
{
    public static void CleanDatabase(this DatabaseContext dbContext)
    {
        // Deletion order respects FK direction: children before the parents they point at.
        // Reference/seed data (Tariffs, TokenPacks, CurrencyRates, Services) is left untouched.
        dbContext.TokenTransactionPurchasedTokenPacks.ExecuteDelete();
        dbContext.TokenTransactions.ExecuteDelete();
        dbContext.TokenTransactionSubscriptionTokenPacks.ExecuteDelete();
        dbContext.BalancePurchasedTokens.ExecuteDelete();
        dbContext.BalanceSubscriptionTokens.ExecuteDelete();
        dbContext.PurchasedTokenPacks.ExecuteDelete();
        dbContext.Subscriptions.ExecuteDelete();
    }
}
