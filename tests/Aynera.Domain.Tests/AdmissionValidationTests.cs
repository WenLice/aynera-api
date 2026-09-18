using Aynera.Domain.Admissions.Enums;
using Aynera.Domain.Admissions.Requests;
using Aynera.Domain.Admissions.Validators;

namespace Aynera.Domain.Tests;

public class AdmissionValidationTests
{
    // Every (state, decision) pair; the expected target is null where the edge must be refused.
    public static TheoryData<AdmissionState, AdmissionDecision, AdmissionState?> TransitionTable()
    {
        var data = new TheoryData<AdmissionState, AdmissionDecision, AdmissionState?>();
        var allowed = new Dictionary<(AdmissionState, AdmissionDecision), AdmissionState>
        {
            [(AdmissionState.Submitted, AdmissionDecision.StartReview)] = AdmissionState.InReview,
            [(AdmissionState.Submitted, AdmissionDecision.Approve)] = AdmissionState.Approved,
            [(AdmissionState.Submitted, AdmissionDecision.Reject)] = AdmissionState.Rejected,
            [(AdmissionState.InReview, AdmissionDecision.Approve)] = AdmissionState.Approved,
            [(AdmissionState.InReview, AdmissionDecision.Reject)] = AdmissionState.Rejected,
            [(AdmissionState.Approved, AdmissionDecision.Reopen)] = AdmissionState.InReview,
            [(AdmissionState.Rejected, AdmissionDecision.Reopen)] = AdmissionState.InReview
        };

        foreach (var state in Enum.GetValues<AdmissionState>())
        {
            foreach (var decision in Enum.GetValues<AdmissionDecision>())
            {
                data.Add(state, decision, allowed.TryGetValue((state, decision), out var to) ? to : null);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TransitionTable))]
    public void TryTransition_MatchesTheFullTable(AdmissionState from, AdmissionDecision decision, AdmissionState? expected)
    {
        var ok = AdmissionValidation.TryTransition(from, decision, out var to);

        Assert.Equal(expected is not null, ok);
        if (expected is not null)
        {
            Assert.Equal(expected, to);
        }
    }

    [Fact]
    public void TryTransition_DraftHasNoStaffEdges()
    {
        foreach (var decision in Enum.GetValues<AdmissionDecision>())
        {
            Assert.False(AdmissionValidation.TryTransition(AdmissionState.Draft, decision, out _));
        }
    }

    [Theory]
    [InlineData(AdmissionState.Draft, true)]
    [InlineData(AdmissionState.Rejected, true)]
    [InlineData(AdmissionState.Submitted, false)]
    [InlineData(AdmissionState.InReview, false)]
    [InlineData(AdmissionState.Approved, false)]
    public void CanSubmit_OnlyFromDraftOrRejected(AdmissionState from, bool expected) =>
        Assert.Equal(expected, AdmissionValidation.CanSubmit(from));

    [Fact]
    public void MinimumAge_IsEighteen() => Assert.Equal(18, AdmissionValidation.MinimumAgeYears);

    [Fact]
    public void AgeInYearsOn_CountsCompletedYearsOnly()
    {
        var dob = new DateOnly(2008, 9, 11);

        Assert.Equal(17, AdmissionValidation.AgeInYearsOn(dob, new DateOnly(2026, 9, 10))); // day before birthday
        Assert.Equal(18, AdmissionValidation.AgeInYearsOn(dob, new DateOnly(2026, 9, 11))); // birthday
        Assert.Equal(18, AdmissionValidation.AgeInYearsOn(dob, new DateOnly(2027, 9, 10)));
    }

    [Fact]
    public void AgeInYearsOn_HandlesLeapDayBirthday()
    {
        var dob = new DateOnly(2008, 2, 29);

        // DateOnly.AddYears clamps 29 Feb to 28 Feb in a non-leap year, so the birthday falls on the 28th.
        Assert.Equal(17, AdmissionValidation.AgeInYearsOn(dob, new DateOnly(2026, 2, 27)));
        Assert.Equal(18, AdmissionValidation.AgeInYearsOn(dob, new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void IsAtLeastMinimumAge_BoundaryIsTheBirthday()
    {
        var dob = new DateOnly(2008, 9, 11);

        Assert.False(AdmissionValidation.IsAtLeastMinimumAge(dob, new DateOnly(2026, 9, 10)));
        Assert.True(AdmissionValidation.IsAtLeastMinimumAge(dob, new DateOnly(2026, 9, 11)));
    }

    [Theory]
    [InlineData("Terms", true)]
    [InlineData("privacy", true)]
    [InlineData("CommunityGuidelines", true)]
    [InlineData("MatchmakingDataUse", false)] // removed on 2026-09-11; must not parse
    [InlineData("", false)]
    [InlineData(null, false)]
    public void BeKnownConsentKind_MatchesExpected(string? value, bool expected) =>
        Assert.Equal(expected, AdmissionValidation.BeKnownConsentKind(value));

    [Fact]
    public void ConsentPolicyKind_HasExactlyTheThreeRequiredDocuments() =>
        Assert.Equal(
            [ConsentPolicyKind.Terms, ConsentPolicyKind.Privacy, ConsentPolicyKind.CommunityGuidelines],
            Enum.GetValues<ConsentPolicyKind>());

    [Theory]
    [InlineData("StartReview", true)]
    [InlineData("approve", true)]
    [InlineData("Reject", true)]
    [InlineData("REOPEN", true)]
    [InlineData("Resubmit", false)]
    [InlineData(null, false)]
    public void BeKnownDecision_MatchesExpected(string? value, bool expected) =>
        Assert.Equal(expected, AdmissionValidation.BeKnownDecision(value));

    [Fact]
    public void DecisionValidator_RejectRequiresReason()
    {
        var validator = new AdmissionDecisionRequestValidator();

        Assert.False(validator.Validate(new AdmissionDecisionRequest("Reject", null, null)).IsValid);
        Assert.False(validator.Validate(new AdmissionDecisionRequest("Reject", "   ", null)).IsValid);
        Assert.True(validator.Validate(new AdmissionDecisionRequest("Reject", "Profile photos do not match video.", null)).IsValid);
    }

    [Fact]
    public void DecisionValidator_ApproveDoesNotRequireReason()
    {
        var result = new AdmissionDecisionRequestValidator().Validate(new AdmissionDecisionRequest("Approve", null, "Looks good"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void DecisionValidator_RejectsUnknownDecisionAndOverlongFields()
    {
        var validator = new AdmissionDecisionRequestValidator();

        Assert.False(validator.Validate(new AdmissionDecisionRequest("Ban", null, null)).IsValid);
        Assert.False(validator.Validate(new AdmissionDecisionRequest("Reject", new string('x', 501), null)).IsValid);
        Assert.False(validator.Validate(new AdmissionDecisionRequest("Approve", null, new string('x', 2001))).IsValid);
    }

    [Fact]
    public void ConsentValidator_RequiresKnownKindAndVersion()
    {
        var validator = new AcceptConsentRequestValidator();

        Assert.True(validator.Validate(new AcceptConsentRequest("Terms", "1.0")).IsValid);
        Assert.False(validator.Validate(new AcceptConsentRequest("Newsletter", "1.0")).IsValid);
        Assert.False(validator.Validate(new AcceptConsentRequest("Terms", "")).IsValid);
        Assert.False(validator.Validate(new AcceptConsentRequest("Terms", new string('9', 33))).IsValid);
    }
}
