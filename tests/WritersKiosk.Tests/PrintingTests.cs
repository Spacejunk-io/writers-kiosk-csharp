// Writer's Kiosk tests — bilingual two-column rendering. GPL-3.0-or-later.
using Xunit;

namespace WritersKiosk.Tests;

public sealed class PrintingTests
{
    private const string Marker = LlmClient.ColumnBreak;

    private const string TwoColumnReport = $"""
        # Writing Feedback

        ## ⭐ Praise (Glow) / Elogios

        - Your thesis names a clear cause.
        - Strong use of the word "innovation".

        {Marker}

        - Tu tesis nombra una causa clara.
        - Buen uso de la palabra "innovación".

        ## ❓ Questions (Grow) / Preguntas

        - How does map A support your claim?

        {Marker}

        - ¿Cómo apoya el mapa A tu afirmación?
        """;

    [Fact]
    public void ReportsWithoutTheMarkerRenderPlainly()
    {
        var html = Printing.RenderBody("# Writing Feedback\n\n## ⭐ Praise (Glow)\n\n- Nice thesis.");
        Assert.DoesNotContain("class=\"cols\"", html);
        Assert.Contains("Nice thesis.", html);
    }

    [Fact]
    public void MarkedReportsRenderOneColumnTablePerSection()
    {
        var html = Printing.RenderBody(TwoColumnReport);
        Assert.Equal(2, CountOf(html, "<table class=\"cols\">"));
        // Both languages present, marker never printed.
        Assert.Contains("innovation", html);
        Assert.Contains("innovación", html);
        Assert.DoesNotContain(Marker, html);
        // Section headings span the page (rendered as h2, outside tables),
        // so each section starts level in both languages.
        Assert.Equal(2, CountOf(html, "<h2"));
        Assert.Contains("<h1", html);
    }

    [Fact]
    public void MatchingItemCountsPairItemsRowByRow()
    {
        var html = Printing.RenderBody(TwoColumnReport);
        // Praise: 2 items per language → 2 paired rows; Questions has a
        // single item per side → 1 row. 3 rows in total.
        Assert.Equal(3, CountOf(html, "<tr>"));
    }

    [Fact]
    public void MismatchedItemCountsFallBackToOneRowPerSection()
    {
        var report = $"""
            ## ⭐ Praise (Glow) / Elogios

            - one
            - two

            {Marker}

            - uno
            - dos
            - tres
            """;
        var html = Printing.RenderBody(report);
        Assert.Equal(1, CountOf(html, "<tr>"));
        Assert.Contains("tres", html);
    }

    [Fact]
    public void MalformedDoubleMarkerNeverReachesPaper()
    {
        var report = $"""
            ## ⭐ Praise (Glow)

            English text
            {Marker}
            Spanish text
            {Marker}
            stray half
            """;
        var html = Printing.RenderBody(report);
        Assert.DoesNotContain(Marker, html);
        Assert.Contains("English text", html);
        Assert.Contains("stray half", html);
    }

    [Fact]
    public void SectionsSplitOnH2HeadingsWithPreambleFirst()
    {
        var sections = Printing.SplitSections("# Title\nintro\n\n## A\nbody a\n\n## B\nbody b");
        Assert.Equal(3, sections.Count);
        Assert.Null(sections[0].Heading);
        Assert.Contains("# Title", sections[0].Body);
        Assert.Equal("## A", sections[1].Heading);
        Assert.Contains("body a", sections[1].Body);
        Assert.Equal("## B", sections[2].Heading);
    }

    [Fact]
    public void BlocksSplitOnListItemsAndParagraphs()
    {
        var blocks = Printing.SplitBlocks("- item one\n  continues here\n- item two\n\nA closing paragraph.");
        Assert.Equal(3, blocks.Count);
        Assert.Contains("continues here", blocks[0]);
        Assert.StartsWith("- item two", blocks[1]);
        Assert.StartsWith("A closing", blocks[2]);
    }

    [Fact]
    public void NumberedItemsSplitTheSameWay()
    {
        var blocks = Printing.SplitBlocks("1. first\n2. second\n3. third");
        Assert.Equal(3, blocks.Count);
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
