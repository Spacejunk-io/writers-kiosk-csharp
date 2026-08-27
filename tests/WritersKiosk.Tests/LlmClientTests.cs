// Writer's Kiosk tests — sentinel detection & prompt assembly. GPL-3.0-or-later.
using Xunit;

namespace WritersKiosk.Tests;

public sealed class LlmClientTests
{
    private static SessionSettings Session(
        string subject = "Social Studies", string? bilingual = null, bool twoColumn = true) =>
        new() { Subject = subject, BilingualLanguage = bilingual, BilingualTwoColumn = twoColumn };

    // ── Refusal / blank / safety sentinels ─────────────────────────

    [Fact]
    public void RefusalSentinelBecomesANoticeNamingTheSubject()
    {
        var notice = LlmClient.NoticeFor(
            "Please submit a Social Studies assignment for feedback.", Session());
        Assert.NotNull(notice);
        Assert.Contains(notice!, line => line.Contains("Social Studies"));
        Assert.Equal("Nothing was printed.", notice![0]);
    }

    [Fact]
    public void BlankPageSentinelBecomesItsNotice()
    {
        var notice = LlmClient.NoticeFor(
            "This page looks blank — write your answers on it, then bring it back for feedback.",
            Session());
        Assert.NotNull(notice);
        Assert.Contains(notice!, line => line.Contains("blank"));
    }

    [Fact]
    public void ARealReportIsNotANotice()
    {
        // Long text (>= 300 chars) is always treated as a real report,
        // even when a sentinel phrase happens to appear inside it.
        var report = "# Writing Feedback\n\n## ⭐ Praise (Glow)\n" +
                     new string('x', 400) + "\nassignment for feedback";
        Assert.Null(LlmClient.NoticeFor(report, Session()));
    }

    [Fact]
    public void ShortNonSentinelTextIsNotANotice() =>
        Assert.Null(LlmClient.NoticeFor("An unexpected short answer.", Session()));

    [Fact]
    public void SafetyFlagIsDetected()
    {
        Assert.True(LlmClient.IsSafetyFlag("[[KIOSK-SAFETY]] Please bring this page to your teacher."));
        Assert.False(LlmClient.IsSafetyFlag("# Writing Feedback\n\nGreat work."));
    }

    // ── System-prompt assembly ─────────────────────────────────────

    [Fact]
    public void TwoColumnBilingualPromptCarriesTheColumnMarker()
    {
        var prompt = LlmClient.BuildSystemPrompt(Session(bilingual: "Spanish", twoColumn: true));
        Assert.Contains(LlmClient.ColumnBreak, prompt);
        Assert.Contains("Spanish", prompt);
        Assert.Contains("two-column", prompt);
    }

    [Fact]
    public void StackedBilingualPromptHasNoColumnMarker()
    {
        var prompt = LlmClient.BuildSystemPrompt(Session(bilingual: "Spanish", twoColumn: false));
        Assert.DoesNotContain(LlmClient.ColumnBreak, prompt);
        Assert.Contains("Spanish", prompt);
    }

    [Fact]
    public void WorldLanguagesAutoBilingualFollowsTheLayoutChoice()
    {
        var columns = LlmClient.BuildSystemPrompt(Session(subject: "World Languages", twoColumn: true));
        Assert.Contains(LlmClient.ColumnBreak, columns);
        var stacked = LlmClient.BuildSystemPrompt(Session(subject: "World Languages", twoColumn: false));
        Assert.DoesNotContain(LlmClient.ColumnBreak, stacked);
    }

    [Fact]
    public void EnglishOnlyPromptHasNoBilingualSection()
    {
        var prompt = LlmClient.BuildSystemPrompt(Session());
        Assert.DoesNotContain("BILINGUAL FEEDBACK", prompt);
        Assert.DoesNotContain(LlmClient.ColumnBreak, prompt);
    }

    [Fact]
    public void ExplicitBilingualChoiceSupersedesWorldLanguagesAutoDetect()
    {
        var prompt = LlmClient.BuildSystemPrompt(
            Session(subject: "World Languages", bilingual: "Arabic", twoColumn: false));
        Assert.Contains("Arabic", prompt);
        Assert.DoesNotContain("language of study", prompt);
    }

    [Fact]
    public void AssignmentContextIsAppendedLast()
    {
        var session = Session();
        session.AssignmentContext = "Today we compare the Erie Canal and the railroads.";
        var prompt = LlmClient.BuildSystemPrompt(session);
        Assert.Contains("TEACHER'S ASSIGNMENT CONTEXT", prompt);
        Assert.EndsWith("Erie Canal and the railroads.", prompt.TrimEnd());
    }

    [Fact]
    public void PromptNamesTheGradeSubjectAndBand()
    {
        var session = new SessionSettings
        {
            Level = Subjects.High, Subject = "Science", Grade = 10, Band = "advanced",
        };
        var prompt = LlmClient.BuildSystemPrompt(session);
        Assert.Contains("grade 10", prompt);
        Assert.Contains("Science", prompt);
        Assert.Contains(Bands.Guidance("advanced"), prompt);
    }
}
