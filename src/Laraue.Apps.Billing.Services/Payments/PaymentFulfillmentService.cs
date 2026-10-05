using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Billing.Services.Payments;

/// <summary>
/// Gives the customer what a paid <see cref="Payment"/> was for. Does not know which provider took
/// the money.
/// </summary>
public interface IPaymentFulfillment
{
    /// <summary>
    /// Applies the purchase to the customer's subscription or token balance. Must run inside the
    /// caller's transaction, which also saves the changes, so the fulfilment and the payment's
    /// status change commit together.
    /// </summary>
    Task FulfillAsync(Payment payment, CancellationToken cancellationToken);
}

public class PaymentFulfillmentService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider) : IPaymentFulfillment
{
    public Task FulfillAsync(Payment payment, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        return payment.Kind switch
        {
            PaymentKind.Subscription => ActivateSubscriptionAsync(payment, cancellationToken),
            PaymentKind.TokenPack => CreditTokenPackAsync(payment, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown payment kind '{payment.Kind}'."),
        };
    }

    /// <summary>
    /// Buying the tariff the customer is already on extends it by one billing period. Buying another
    /// tariff replaces the current subscription right away: the old one is cancelled, the unused free
    /// allowance moves to the new one.
    /// </summary>
    private async Task ActivateSubscriptionAsync(Payment payment, CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        // The same key SubscriptionService locks on, so a concurrent provisioning waits for us.
        await context.Database.PgAdvisoryXactLock(payment.PaidEntityId.ToString(), cancellationToken);

        var tariff = await context.Tariffs
            .Where(x => x.Id == payment.TariffId)
            .Select(x => new { x.Id, x.IncludedTokensCount, x.BillingPeriod })
            .SingleAsync(cancellationToken);

        var current = await context.Subscriptions
            .Where(x => x.ServiceId == payment.ServiceId
                && x.PaidEntityId == payment.PaidEntityId
                && x.Status == SubscriptionStatus.Active
                && (x.CurrentPeriodFinishesAt == null || x.CurrentPeriodFinishesAt > now))
            .SingleOrDefaultAsync(cancellationToken);

        if (current is not null && current.TariffId == tariff.Id)
        {
            var balance = await context.BalanceSubscriptionTokens
                .SingleAsync(x => x.SubscriptionId == current.Id, cancellationToken);

            current.CurrentPeriodFinishesAt = GetPeriodFinish(
                current.CurrentPeriodFinishesAt ?? now,
                tariff.BillingPeriod);
            balance.SubscriptionTokensCount += tariff.IncludedTokensCount;
        }
        else
        {
            var carriedFreeTokensCount = 0L;

            if (current is not null)
            {
                carriedFreeTokensCount = await context.BalanceSubscriptionTokens
                    .Where(x => x.SubscriptionId == current.Id)
                    .Select(x => x.FreeTokensCount)
                    .SingleAsync(cancellationToken);

                current.Status = SubscriptionStatus.Cancelled;
                current.CurrentPeriodFinishesAt = now;
            }

            var subscriptionId = Guid.NewGuid();

            context.Subscriptions.Add(new Subscription
            {
                Id = subscriptionId,
                ServiceId = payment.ServiceId,
                TariffId = tariff.Id,
                OwnerId = payment.OwnerId,
                PaidEntityId = payment.PaidEntityId,
                Status = SubscriptionStatus.Active,
                CurrentPeriodStartedAt = now,
                CurrentPeriodFinishesAt = GetPeriodFinish(now, tariff.BillingPeriod),
            });

            context.BalanceSubscriptionTokens.Add(new BalanceSubscriptionToken
            {
                SubscriptionId = subscriptionId,
                SubscriptionTokensCount = tariff.IncludedTokensCount,
                FreeTokensCount = carriedFreeTokensCount,
            });
        }

        AddLedgerRow(payment, TokenTransactionReason.TariffGrant, tariff.IncludedTokensCount, now);
    }

    private async Task CreditTokenPackAsync(Payment payment, CancellationToken cancellationToken)
    {
        var now = dateTimeProvider.UtcNow;

        await context.Database.PgAdvisoryXactLock(payment.PaidEntityId.ToString(), cancellationToken);

        var pack = await context.TokenPacks
            .Where(x => x.Id == payment.TokenPackId)
            .Select(x => new { x.Id, x.TokensCount, x.ExpirationDuration, x.ExpirationPeriod })
            .SingleAsync(cancellationToken);

        context.PurchasedTokenPacks.Add(new PurchasedTokenPack
        {
            Id = Guid.NewGuid(),
            PaidEntityId = payment.PaidEntityId,
            TokenPackId = pack.Id,
            PurchasedAt = now,
            ExpiredAt = pack.ExpirationPeriod == BillingPeriod.Month
                ? now.AddMonths(pack.ExpirationDuration)
                : now.AddYears(100),
        });

        var balance = await context.BalancePurchasedTokens
            .SingleOrDefaultAsync(x => x.PaidEntityId == payment.PaidEntityId, cancellationToken);

        if (balance is null)
        {
            context.BalancePurchasedTokens.Add(new BalancePurchasedToken
            {
                PaidEntityId = payment.PaidEntityId,
                Balance = pack.TokensCount,
            });
        }
        else
        {
            balance.Balance += pack.TokensCount;
        }

        AddLedgerRow(payment, TokenTransactionReason.Purchase, pack.TokensCount, now);
    }

    private static DateTime? GetPeriodFinish(DateTime start, BillingPeriod period)
        => period == BillingPeriod.Month ? start.AddMonths(1) : null;

    private void AddLedgerRow(Payment payment, TokenTransactionReason reason, long delta, DateTime now)
    {
        context.TokenTransactions.Add(new TokenTransaction
        {
            Id = Guid.NewGuid(),
            PaidEntityId = payment.PaidEntityId,
            OwnerId = payment.OwnerId,
            Status = TokenSpentStatus.Confirmed,
            Reason = reason,
            CreatedAt = now,
            FinishedAt = now,
            Delta = delta,
        });
    }
}
