// Writer's Kiosk tests — catalog completeness. GPL-3.0-or-later.
using Xunit;

namespace WritersKiosk.Tests;

public sealed class SubjectsAndBandsTests
{
    // The generic fallback line — no cataloged subject may fall through
    // to it, or that class silently loses its tailored guidance.
    private static readonly string Fallback = Subjects.Guidance("nope", "nope");

    [Fact]
    public void EveryMiddleSchoolSubjectHasTailoredGuidance()
    {
        foreach (var subject in Subjects.MiddleSubjects)
            Assert.NotEqual(Fallback, Subjects.Guidance(Subjects.Middle, subject));
    }

    [Fact]
    public void EveryHighSchoolSubjectHasTailoredGuidance()
    {
        foreach (var subject in Subjects.HighSubjects)
            Assert.NotEqual(Fallback, Subjects.Guidance(Subjects.High, subject));
    }

    [Fact]
    public void SocialStudiesExistsInBothCatalogs()
    {
        // Grade changes fall back to "Social Studies" when the new
        // level's catalog lacks the old subject — it must exist in both.
        Assert.Contains("Social Studies", Subjects.MiddleSubjects);
        Assert.Contains("Social Studies", Subjects.HighSubjects);
    }

    [Fact]
    public void AllFiveBandsExistWithDistinctGuidance()
    {
        Assert.Equal(5, Bands.All.Length);
        var texts = Bands.All.Select(b => Bands.Guidance(b.Key)).ToArray();
        Assert.All(texts, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Equal(texts.Length, texts.Distinct().Count());
    }

    [Fact]
    public void UnknownBandFallsBackToOnGradeLevel()
    {
        Assert.Equal("On grade level", Bands.Display("no-such-band"));
        Assert.Contains("at grade level", Bands.Guidance("no-such-band"));
    }

    [Fact]
    public void BandDisplayNamesAreAssetBased()
    {
        // The band language is deliberately respectful — "Emerging",
        // never deficit phrasing like "below grade level".
        Assert.DoesNotContain(Bands.All, b => b.Display.Contains("below", StringComparison.OrdinalIgnoreCase));
    }
}
