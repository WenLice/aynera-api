using Aynera.Domain.Auth.Enums;
using Aynera.Domain.Preferences.Enums;
using Aynera.Domain.Preferences.Validators;

namespace Aynera.Domain.Tests;

public class HardFiltersTests
{
    private static readonly Guid Bangalore = Guid.NewGuid();
    private static readonly Guid Delhi = Guid.NewGuid();

    private static MatchCandidate Person(
        Gender gender = Gender.Female,
        int age = 28,
        Guid? cityId = null,
        InterestedIn interestedIn = InterestedIn.Everyone,
        int minAge = 18,
        int maxAge = 45,
        bool flexible = false,
        IntentOutcome outcome = IntentOutcome.Prospect) =>
        new(Guid.NewGuid(), gender, age, cityId ?? Bangalore, interestedIn, minAge, maxAge, flexible, outcome);

    [Fact]
    public void CompatiblePair_Passes()
    {
        var result = HardFilters.Evaluate(Person(Gender.Female), Person(Gender.Male));

        Assert.True(result.Passes);
        Assert.Empty(result.Reasons);
    }

    /// <summary>M-01: evaluating a pair either way round must give the same verdict.</summary>
    [Theory]
    [InlineData(Gender.Female, InterestedIn.Male, Gender.Male, InterestedIn.Female)]
    [InlineData(Gender.Female, InterestedIn.Male, Gender.Male, InterestedIn.Male)]
    [InlineData(Gender.Other, InterestedIn.Everyone, Gender.Male, InterestedIn.Other)]
    [InlineData(Gender.Other, InterestedIn.Female, Gender.Male, InterestedIn.Everyone)]
    public void Evaluation_IsSymmetric(Gender aGender, InterestedIn aWants, Gender bGender, InterestedIn bWants)
    {
        var a = Person(aGender, interestedIn: aWants);
        var b = Person(bGender, interestedIn: bWants);

        Assert.Equal(HardFilters.Evaluate(a, b).Passes, HardFilters.Evaluate(b, a).Passes);
    }

    // ---------- gender ----------

    [Theory]
    [InlineData(InterestedIn.Male, Gender.Male, true)]
    [InlineData(InterestedIn.Male, Gender.Female, false)]
    [InlineData(InterestedIn.Male, Gender.Other, false)]
    [InlineData(InterestedIn.Female, Gender.Female, true)]
    [InlineData(InterestedIn.Other, Gender.Other, true)]
    [InlineData(InterestedIn.Other, Gender.Male, false)]
    [InlineData(InterestedIn.Everyone, Gender.Male, true)]
    [InlineData(InterestedIn.Everyone, Gender.Female, true)]
    [InlineData(InterestedIn.Everyone, Gender.Other, true)]
    public void Everyone_CoversAllThreeGenders(InterestedIn wants, Gender gender, bool expected) =>
        Assert.Equal(expected, PreferenceRules.Accepts(wants, gender));

    [Fact]
    public void OneSidedInterest_IsNotEnough()
    {
        // She is open to him; he is not open to her.
        var a = Person(Gender.Female, interestedIn: InterestedIn.Male);
        var b = Person(Gender.Male, interestedIn: InterestedIn.Male);

        var result = HardFilters.Evaluate(a, b);

        Assert.False(result.Passes);
        Assert.Contains(HardFilterReasons.GenderNotReciprocal, result.Reasons);
    }

    /// <summary>A member who chose third gender must be reachable — they were not, before Everyone covered them.</summary>
    [Fact]
    public void ThirdGender_IsReachable()
    {
        var them = Person(Gender.Other, interestedIn: InterestedIn.Everyone);

        Assert.True(HardFilters.Evaluate(Person(Gender.Male, interestedIn: InterestedIn.Other), them).Passes);
        Assert.True(HardFilters.Evaluate(Person(Gender.Male, interestedIn: InterestedIn.Everyone), them).Passes);
    }

    // ---------- age ----------

    [Theory]
    [InlineData(24, 32, 28, false, true)]
    [InlineData(24, 32, 33, false, false)]
    [InlineData(24, 32, 23, false, false)]
    [InlineData(24, 32, 34, true, true)]   // flexible widens the top by two
    [InlineData(24, 32, 22, true, true)]   // and the bottom
    [InlineData(24, 32, 35, true, false)]  // but only by two
    public void Flexible_WidensTheRangeByTwoYears(int min, int max, int otherAge, bool flexible, bool expected) =>
        Assert.Equal(expected, HardFilters.AgeAccepts(Person(minAge: min, maxAge: max, flexible: flexible), otherAge));

    [Fact]
    public void AgeMustBeReciprocal()
    {
        var older = Person(Gender.Male, age: 40, minAge: 25, maxAge: 45);
        // She is inside his range; he is outside hers.
        var younger = Person(Gender.Female, age: 26, minAge: 24, maxAge: 30);

        var result = HardFilters.Evaluate(older, younger);

        Assert.False(result.Passes);
        Assert.Contains(HardFilterReasons.AgeNotReciprocal, result.Reasons);
    }

    // ---------- city ----------

    [Fact]
    public void DifferentCities_AreNotIntroduced()
    {
        var result = HardFilters.Evaluate(
            Person(Gender.Female, cityId: Bangalore),
            Person(Gender.Male, cityId: Delhi));

        Assert.False(result.Passes);
        Assert.Contains(HardFilterReasons.DifferentCity, result.Reasons);
    }

    // ---------- intent ----------

    /// <summary>Exact outcome only: 4 of the 16 pairings survive, and they are the diagonal.</summary>
    [Fact]
    public void Intent_MatchesOnlyTheExactOutcome()
    {
        var outcomes = Enum.GetValues<IntentOutcome>();
        var passing = 0;

        foreach (var mine in outcomes)
        {
            foreach (var theirs in outcomes)
            {
                var result = HardFilters.Evaluate(
                    Person(Gender.Female, outcome: mine),
                    Person(Gender.Male, outcome: theirs));

                Assert.Equal(mine == theirs, result.Passes);
                if (result.Passes) passing++;
                else if (mine != theirs)
                {
                    Assert.Contains(HardFilterReasons.IntentNotCompatible, result.Reasons);
                }
            }
        }

        Assert.Equal(4, passing);
        Assert.Equal(16, outcomes.Length * outcomes.Length);
    }

    /// <summary>Same track is not enough on its own — Platonic and Spontaneous are both Fluid.</summary>
    [Fact]
    public void SameTrack_DoesNotMatchAcrossDifferentOutcomes()
    {
        Assert.Equal(
            PreferenceRules.TrackFor(IntentOutcome.Platonic),
            PreferenceRules.TrackFor(IntentOutcome.Spontaneous));

        var result = HardFilters.Evaluate(
            Person(Gender.Female, outcome: IntentOutcome.Platonic),
            Person(Gender.Male, outcome: IntentOutcome.Spontaneous));

        Assert.False(result.Passes);
    }

    [Theory]
    [InlineData(IntentOutcome.Platonic, RelationshipTrack.Fluid)]
    [InlineData(IntentOutcome.Spontaneous, RelationshipTrack.Fluid)]
    [InlineData(IntentOutcome.Prospect, RelationshipTrack.Intent)]
    [InlineData(IntentOutcome.Legacy, RelationshipTrack.Intent)]
    public void TrackIsDerivedFromOutcome(IntentOutcome outcome, RelationshipTrack expected) =>
        Assert.Equal(expected, PreferenceRules.TrackFor(outcome));

    // ---------- reporting ----------

    /// <summary>§6.2 needs to know which filter emptied a pool, so every failing filter is reported.</summary>
    [Fact]
    public void EveryFailingFilter_IsReported()
    {
        var a = Person(Gender.Female, age: 28, cityId: Bangalore,
            interestedIn: InterestedIn.Female, minAge: 24, maxAge: 30, outcome: IntentOutcome.Platonic);
        var b = Person(Gender.Male, age: 44, cityId: Delhi,
            interestedIn: InterestedIn.Male, minAge: 40, maxAge: 45, outcome: IntentOutcome.Legacy);

        var result = HardFilters.Evaluate(a, b);

        Assert.False(result.Passes);
        Assert.Contains(HardFilterReasons.GenderNotReciprocal, result.Reasons);
        Assert.Contains(HardFilterReasons.AgeNotReciprocal, result.Reasons);
        Assert.Contains(HardFilterReasons.DifferentCity, result.Reasons);
        Assert.Contains(HardFilterReasons.IntentNotCompatible, result.Reasons);
    }

    [Fact]
    public void AMemberIsNeverIntroducedToThemselves()
    {
        var me = Person();

        var result = HardFilters.Evaluate(me, me);

        Assert.False(result.Passes);
        Assert.Equal([HardFilterReasons.SameMember], result.Reasons);
    }
}
