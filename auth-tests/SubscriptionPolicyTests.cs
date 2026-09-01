using RestaurantAgent.CentralApi;
using Xunit;

namespace RestaurantAgent.Auth.Tests;

public sealed class SubscriptionPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Trial_is_available_before_15_day_deadline()
    {
        var result = SubscriptionPolicy.Evaluate("BASIC", Now.AddDays(1), null, false, Now);
        Assert.Equal("TRIAL", result.Status);
        Assert.True(result.CanAccessContent);
        Assert.Equal(1, result.TrialDaysRemaining);
    }

    [Fact]
    public void Expired_trial_without_payment_blocks_content()
    {
        var result = SubscriptionPolicy.Evaluate("BASIC", Now.AddSeconds(-1), null, false, Now);
        Assert.Equal("EXPIRED", result.Status);
        Assert.False(result.CanAccessContent);
    }

    [Fact]
    public void Paid_period_keeps_access_after_trial()
    {
        var result = SubscriptionPolicy.Evaluate("PLUS", Now.AddDays(-30), Now.AddMonths(3), false, Now);
        Assert.Equal("ACTIVE", result.Status);
        Assert.True(result.CanAccessContent);
    }

    [Fact]
    public void Manual_suspension_blocks_even_a_paid_account()
    {
        var result = SubscriptionPolicy.Evaluate("PLUS", Now.AddDays(1), Now.AddMonths(6), true, Now);
        Assert.Equal("SUSPENDED", result.Status);
        Assert.False(result.CanAccessContent);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(6)]
    public void Only_supported_activation_durations_are_accepted(int months) =>
        Assert.True(SubscriptionPolicy.IsValidDuration(months));
}
