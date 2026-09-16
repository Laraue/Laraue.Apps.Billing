using Laraue.Apps.Billing.DataAccess;
using Laraue.Apps.Billing.DataAccess.Data;
using Laraue.Apps.Billing.DataAccess.Entities;
using Laraue.Apps.Billing.IntegrationTests.Infrastructure;
using Laraue.Apps.Billing.Services;
using Laraue.Apps.Billing.Services.Jobs;
using Laraue.Apps.Billing.WebApiHost;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Laraue.Apps.Billing.IntegrationTests;

public class CancelStaleTokenReservationsJobTests : BillingIntegrationTest
{
    private static readonly Guid PlusTariffId = LaraueBoardsTariffsData.PersonalTariffs[1].Tariff.Id;

    private readonly WebApiTestHost _host;
    private readonly FakeDateTimeProvider _dateTimeProvider;
    private readonly ITokenService _tokenService;

    public CancelStaleTokenReservationsJobTests(WebApiTestHost host) : base(host)
    {
        _host = host;
        _dateTimeProvider = new FakeDateTimeProvider(DateTime.UtcNow);
        _tokenService = new TokenService(Context, new SubscriptionService(Context, _dateTimeProvider), _dateTimeProvider);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCancelReservation_WhenOlderThanThreshold()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 1000);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);

        await BackdateTransactionAsync(result.TokenTransactionId!.Value, TimeSpan.FromMinutes(31));

        var job = new CancelStaleTokenReservationsJob(Context, _tokenService, _dateTimeProvider, NullLogger<CancelStaleTokenReservationsJob>.Instance);
        await job.ExecuteAsync(new JobState<EmptyJobData> { JobName = "CancelStaleTokenReservationsJob" }, CancellationToken.None);

        var transaction = await FreshContext().TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Canceled, transaction.Status);

        var balance = await FreshContext().BalanceSubscriptionTokens.SingleAsync();
        Assert.Equal(1000, balance.FreeTokensCount);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotCancelReservation_WhenWithinThreshold()
    {
        var paidEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(paidEntityId, freeTokens: 1000);

        var result = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, paidEntityId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);

        await BackdateTransactionAsync(result.TokenTransactionId!.Value, TimeSpan.FromMinutes(10));

        var job = new CancelStaleTokenReservationsJob(Context, _tokenService, _dateTimeProvider, NullLogger<CancelStaleTokenReservationsJob>.Instance);
        await job.ExecuteAsync(new JobState<EmptyJobData> { JobName = "CancelStaleTokenReservationsJob" }, CancellationToken.None);

        var transaction = await FreshContext().TokenTransactions.SingleAsync(t => t.Id == result.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Started, transaction.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldContinuePastOneFailure_WhenOneRowThrows()
    {
        var firstEntityId = Guid.NewGuid();
        var secondEntityId = Guid.NewGuid();
        await SeedActiveSubscriptionAsync(firstEntityId, freeTokens: 1000);
        await SeedActiveSubscriptionAsync(secondEntityId, freeTokens: 1000);

        var first = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, firstEntityId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);
        var second = await _tokenService.TryReservePersonalTokensAsync(
            ServiceId.LaraueBoards, secondEntityId, inputTokensCount: 100, maxOutputTokensCount: 200, CancellationToken.None);

        await BackdateTransactionAsync(first.TokenTransactionId!.Value, TimeSpan.FromMinutes(31));
        await BackdateTransactionAsync(second.TokenTransactionId!.Value, TimeSpan.FromMinutes(31));

        var tokenServiceMock = new Mock<ITokenService>();
        tokenServiceMock
            .Setup(x => x.CancelTokensReservationAsync(first.TokenTransactionId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        tokenServiceMock
            .Setup(x => x.CancelTokensReservationAsync(second.TokenTransactionId!.Value, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((Guid id, string error, CancellationToken ct) => _tokenService.CancelTokensReservationAsync(id, error, ct));

        var job = new CancelStaleTokenReservationsJob(Context, tokenServiceMock.Object, _dateTimeProvider, NullLogger<CancelStaleTokenReservationsJob>.Instance);
        await job.ExecuteAsync(new JobState<EmptyJobData> { JobName = "CancelStaleTokenReservationsJob" }, CancellationToken.None);

        var secondTransaction = await FreshContext().TokenTransactions.SingleAsync(t => t.Id == second.TokenTransactionId);
        Assert.Equal(TokenSpentStatus.Canceled, secondTransaction.Status);
    }

    private DatabaseContext FreshContext() => _host.Services.CreateScope().ServiceProvider.GetRequiredService<DatabaseContext>();

    private async Task BackdateTransactionAsync(Guid tokenTransactionId, TimeSpan age)
    {
        var transaction = await Context.TokenTransactions.SingleAsync(t => t.Id == tokenTransactionId);
        transaction.CreatedAt = _dateTimeProvider.UtcNow - age;
        await Context.SaveChangesAsync();
    }

    private async Task<Guid> SeedActiveSubscriptionAsync(Guid paidEntityId, long freeTokens)
    {
        var subscriptionId = Guid.NewGuid();

        Context.Subscriptions.Add(new Subscription
        {
            Id = subscriptionId,
            ServiceId = ServiceId.LaraueBoards,
            TariffId = PlusTariffId,
            OwnerId = paidEntityId,
            PaidEntityId = paidEntityId,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStartedAt = DateTime.UtcNow.AddDays(-1),
            CurrentPeriodFinishesAt = DateTime.UtcNow.AddDays(29),
        });

        Context.BalanceSubscriptionTokens.Add(new BalanceSubscriptionToken
        {
            SubscriptionId = subscriptionId,
            FreeTokensCount = freeTokens,
            SubscriptionTokensCount = 0,
        });

        await Context.SaveChangesAsync();

        return subscriptionId;
    }

    private sealed class FakeDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = utcNow;
        public DateTimeOffset UtcOffsetNow => UtcNow;
    }
}
