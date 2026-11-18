using HrService.Domain.Rules;
using Xunit;

namespace HrService.Tests;

public sealed class LeavePolicyTimelineTests
{
    private static readonly PolicyVersion Old = new(new DateOnly(2027, 1, 1), 15m);
    private static readonly PolicyVersion New = new(new DateOnly(2027, 7, 1), 8m);

    [Theory]
    [InlineData("2026-12-31", null)]
    [InlineData("2027-01-01", 15)]
    [InlineData("2027-06-30", 15)]
    [InlineData("2027-07-01", 8)]
    [InlineData("2027-12-31", 8)]
    public void EffectiveOn_UsesLatestVersionStartedOnOrBeforeDate(string date, int? expectedDays)
    {
        var effective = LeavePolicyTimeline.EffectiveOn(new[] { Old, New }, DateOnly.Parse(date));
        Assert.Equal(expectedDays, effective is null ? null : (int)effective.Value.DaysPerYear);
    }

    [Fact]
    public void EffectiveOn_IgnoresInputOrder()
    {
        var effective = LeavePolicyTimeline.EffectiveOn(new[] { New, Old }, new DateOnly(2027, 8, 1));
        Assert.Equal(8m, effective!.Value.DaysPerYear);
    }

    [Fact]
    public void CanAddVersion_RejectsPastEffectiveDate()
    {
        Assert.False(LeavePolicyTimeline.CanAddVersion(new DateOnly(2027, 1, 9), new DateOnly(2027, 1, 10), 2027));
    }

    [Fact]
    public void CanAddVersion_AcceptsTodayAndFutureInSameYear()
    {
        Assert.True(LeavePolicyTimeline.CanAddVersion(new DateOnly(2027, 1, 10), new DateOnly(2027, 1, 10), 2027));
        Assert.True(LeavePolicyTimeline.CanAddVersion(new DateOnly(2027, 12, 1), new DateOnly(2027, 1, 10), 2027));
    }

    [Fact]
    public void CanAddVersion_RejectsDateOutsidePolicyYear()
    {
        Assert.False(LeavePolicyTimeline.CanAddVersion(new DateOnly(2028, 1, 1), new DateOnly(2027, 1, 10), 2027));
    }

    [Fact]
    public void HasSameEffectiveDate_DetectsDuplicate()
    {
        Assert.True(LeavePolicyTimeline.HasSameEffectiveDate(new[] { Old, New }, new DateOnly(2027, 7, 1)));
        Assert.False(LeavePolicyTimeline.HasSameEffectiveDate(new[] { Old, New }, new DateOnly(2027, 8, 1)));
    }
}
