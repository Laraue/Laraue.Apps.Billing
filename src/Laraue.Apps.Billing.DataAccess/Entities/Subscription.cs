namespace Laraue.Apps.Billing.DataAccess.Entities;

public class Subscription
{
    public required Guid Id { get; set; }
    
    public ServiceId ServiceId { get; set; }
    public Service? Service { get; set; }
    
    public Guid TariffId { get; set; }
    public Tariff? Tariff { get; set; }
    
    /// <summary>
    /// Use who paid for this subscription.
    /// </summary>
    public Guid OwnerId { get; set; }
    
    /// <summary>
    /// Identifier of the entity that has been paid.
    /// E.g. userId or organizationId.
    /// </summary>
    public Guid PaidEntityId { get; set; }

    public int? Seats { get; set; }
    
    public SubscriptionStatus Status { get; set; }
    
    public DateTime CurrentPeriodStartedAt { get; set; }
    public DateTime CurrentPeriodFinishesAt { get; set; }
}

public enum SubscriptionStatus
{
    Active,
    Cancelled,
}