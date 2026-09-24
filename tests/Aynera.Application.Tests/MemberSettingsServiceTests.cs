using Aynera.Application.Features.Settings.Services.Implementations;
using Aynera.Domain.Settings.Records;
using Aynera.Domain.Settings.Requests;

namespace Aynera.Application.Tests;

/// <summary>How a settings change merges into what is stored.</summary>
public class MemberSettingsServiceTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Friday = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnsentFields_KeepTheirValue()
    {
        var current = MemberSettingsRecord.Empty(Guid.NewGuid()) with { NotifyReplies = false, NotifyIntroductions = true };

        var next = MemberSettingsService.Apply(current, new UpdateMemberSettingsRequest(NotifyWeekendSurprise: true), Monday);

        Assert.False(next.NotifyReplies);
        Assert.True(next.NotifyIntroductions);
        Assert.True(next.NotifyWeekendSurprise);
    }

    [Fact]
    public void Visibility_MergesPerField()
    {
        var current = MemberSettingsRecord.Empty(Guid.NewGuid()) with
        {
            Visibility = new Dictionary<string, bool> { ["gender"] = false, ["lifestyle.drink"] = true },
        };

        var next = MemberSettingsService.Apply(
            current,
            new UpdateMemberSettingsRequest(Visibility: new Dictionary<string, bool> { ["lifestyle.drink"] = false }),
            Monday);

        Assert.False(next.Visibility["gender"]);
        Assert.False(next.Visibility["lifestyle.drink"]);
    }

    [Fact]
    public void Pause_RecordsWhenItBegan_OnlyOnARealChange()
    {
        var start = MemberSettingsService.Apply(
            MemberSettingsRecord.Empty(Guid.NewGuid()), new UpdateMemberSettingsRequest(IntroductionsPaused: true), Monday);
        Assert.Equal(Monday, start.PausedAtUtc);

        var again = MemberSettingsService.Apply(start, new UpdateMemberSettingsRequest(IntroductionsPaused: true), Friday);
        Assert.Equal(Monday, again.PausedAtUtc);

        var resumed = MemberSettingsService.Apply(again, new UpdateMemberSettingsRequest(IntroductionsPaused: false), Friday);
        Assert.False(resumed.IntroductionsPaused);
        Assert.Null(resumed.PausedAtUtc);
    }
}
