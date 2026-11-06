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

    /// <summary>
    /// Extra storage on top of the tariff's, paid together with it. Null when none was bought.
    /// </summary>
    public Guid? StorageAddonId { get; set; }
    public StorageAddon? StorageAddon { get; set; }

    public int? Seats { get; set; }
    
    public SubscriptionStatus Status { get; set; }
    
    public DateTime CurrentPeriodStartedAt { get; set; }

    /// <summary>
    /// Null means the subscription never expires on its own (a <see cref="BillingPeriod.Forever"/>
    /// tariff, e.g. Free) - there's nothing to renew, so there's no real date to store here.
    /// </summary>
    public DateTime? CurrentPeriodFinishesAt { get; set; }
}

public enum SubscriptionStatus
{
    Active,
    Cancelled,
}