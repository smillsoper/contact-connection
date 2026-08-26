using ContactConnection.Domain.Entities;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Queue Acceleration's actual formula — a waiting caller's effective priority rises over time so
/// a long-waiting call on a lower-priority campaign eventually outranks a freshly-arrived call on
/// a higher-priority one. Campaign.Priority alone (never boosted) is the "off" case. Pure/static
/// so it's testable without a DB or Redis.
/// </summary>
public static class EffectivePriorityCalculator
{
    public static int Compute(Campaign campaign, double secondsWaited)
    {
        if (!campaign.QueueAccelerationEnabled) return campaign.Priority;

        var intervalsElapsed = (int)Math.Floor(secondsWaited / campaign.QueueAccelerationIntervalSeconds);
        return campaign.Priority + intervalsElapsed * campaign.QueueAccelerationPriorityBoost;
    }
}
