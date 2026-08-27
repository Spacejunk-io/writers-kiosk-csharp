// Writer's Kiosk (C#) — Markdown → HTML → PDF → printer. GPL-3.0-or-later.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace WritersKiosk;

public static class Printing
{
    /// <summary>
    /// Renders the Markdown feedback to styled HTML, converts it to PDF
    /// with a locally installed Chromium-based browser (Edge ships with
    /// Windows), and sends it to the printer with no user interaction.
    /// Temp files hold only the feedback text (never the captured image)
    /// and are deleted afterwards.
    /// </summary>
    public static void PrintMarkdown(string markdown, KioskConfig cfg, string subject)
    {
        var htmlBody = RenderBody(markdown);
        var page = WrapInPage(htmlBody, subject);

        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var htmlPath = Path.Combine(Path.GetTempPath(), $"kiosk_report_{stamp}.html");
        var pdfPath = Path.Combine(Path.GetTempPath(), $"kiosk_report_{stamp}.pdf");

        File.WriteAllText(htmlPath, page);
        try
        {
            HtmlToPdf(htmlPath, pdfPath);
            DispatchToPrinter(pdfPath, cfg);
        }
        finally
        {
            try { File.Delete(htmlPath); } catch { }
            try { File.Delete(pdfPath); } catch { }
        }
    }

    /// <summary>
    /// Markdown → HTML body. A report carrying the bilingual
    /// <see cref="LlmClient.ColumnBreak"/> marker is laid out in two
    /// side-by-side columns; anything else renders normally. The
    /// markdown itself says which, so a reprint always matches the
    /// original layout.
    /// </summary>
    internal static string RenderBody(string markdown) =>
        markdown.Contains(LlmClient.ColumnBreak)
            ? RenderTwoColumn(markdown)
            : Markdig.Markdown.ToHtml(markdown);

    /// <summary>
    /// Two-column bilingual layout. Each "## " section whose body holds
    /// exactly one ColumnBreak becomes a full-width heading plus a
    /// two-column table: first language left, second language right.
    /// Because every section heading spans the page, Praise, Questions,
    /// Polish, and the Accuracy Check each begin at the same point in
    /// both languages — paragraph lengths may drift within a section
    /// (translations differ), and that drift stays visible and easy to
    /// follow. When both halves contain the same number of items, the
    /// items themselves are paired row by row, so paragraphs sit
    /// roughly side by side too. Sections without the marker (title,
    /// or a section the model wrote once) render full-width unchanged.
    /// </summary>
    internal static string RenderTwoColumn(string markdown)
    {
        var html = new StringBuilder();
        foreach (var (heading, body) in SplitSections(markdown))
        {
            var raw = heading is null ? body : heading + "\n" + body;
            var halves = body.Split(LlmClient.ColumnBreak);
            if (halves.Length != 2)
            {
                // No marker (or a malformed extra one): render as-is,
                // minus any stray marker text — it must never print.
                html.Append(Markdig.Markdown.ToHtml(raw.Replace(LlmClient.ColumnBreak, "")));
                continue;
            }

            if (heading is not null)
                html.Append(Markdig.Markdown.ToHtml(heading));
            html.Append("<table class=\"cols\">");
            var left = SplitBlocks(halves[0]);
            var right = SplitBlocks(halves[1]);
            if (left.Count == right.Count && left.Count > 1)
            {
                // Same item count on both sides: pair item with item.
                for (var i = 0; i < left.Count; i++)
                    html.Append("<tr><td>").Append(Markdig.Markdown.ToHtml(left[i]))
                        .Append("</td><td>").Append(Markdig.Markdown.ToHtml(right[i]))
                        .Append("</td></tr>");
            }
            else
            {
                html.Append("<tr><td>").Append(Markdig.Markdown.ToHtml(halves[0].Trim()))
                    .Append("</td><td>").Append(Markdig.Markdown.ToHtml(halves[1].Trim()))
                    .Append("</td></tr>");
            }
            html.Append("</table>");
        }
        return html.ToString();
    }

    /// <summary>Splits a report at its "## " headings. The first tuple
    /// (heading null) is everything before the first section — the
    /// report title and any preamble.</summary>
    internal static List<(string? Heading, string Body)> SplitSections(string markdown)
    {
        var sections = new List<(string?, string)>();
        string? heading = null;
        var body = new StringBuilder();
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## "))
            {
                if (heading is not null || body.ToString().Trim().Length > 0)
                    sections.Add((heading, body.ToString()));
                heading = line;
                body.Clear();
            }
            else
            {
                body.AppendLine(line);
            }
        }
        if (heading is not null || body.ToString().Trim().Length > 0)
            sections.Add((heading, body.ToString()));
        return sections;
    }

    private static readonly Regex ListItemStart =
        new(@"^\s{0,3}([-*+]|\d{1,3}[.)])\s", RegexOptions.Compiled);

    /// <summary>Splits section content into its top-level blocks: each
    /// list item (with its continuation lines) or blank-line-separated
    /// paragraph is one block.</summary>
    internal static List<string> SplitBlocks(string markdown)
    {
        var blocks = new List<string>();
        var current = new StringBuilder();
        var afterBlank = true;

        void Flush()
        {
            var text = current.ToString().Trim();
            if (text.Length > 0) blocks.Add(text);
            current.Clear();
        }

        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                afterBlank = true;
                continue;
            }
            if (ListItemStart.IsMatch(line) || afterBlank)
                Flush();
            current.AppendLine(line);
            afterBlank = false;
        }
        Flush();
        return blocks;
    }

    internal static string WrapInPage(string body, string subject)
    {
        var date = DateTime.Now.ToString("MMMM d, yyyy");
        return $$"""
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  @page { size: letter; margin: 0.9in 1in; }
  body {
    font-family: Georgia, 'Times New Roman', serif;
    font-size: 12.5pt;
    line-height: 1.55;
    color: #1a1a1a;
    max-width: 6.5in;
    margin: 0 auto;
  }
  h1 {
    font-size: 20pt;
    border-bottom: 2.5px solid #1a1a1a;
    padding-bottom: 6px;
    margin-bottom: 4px;
  }
  h2 { font-size: 14pt; margin-top: 20px; margin-bottom: 6px; }
  blockquote {
    border-left: 3px solid #999;
    margin-left: 0;
    padding-left: 12px;
    color: #333;
    font-style: italic;
  }
  .meta { color: #555; font-size: 10pt; margin-bottom: 14px; }
  /* Bilingual two-column layout: each section heading spans the page,
     then the two languages sit side by side with a thin divider. */
  table.cols { width: 100%; border-collapse: collapse; table-layout: fixed; }
  table.cols tr { page-break-inside: avoid; }
  table.cols td {
    width: 50%;
    vertical-align: top;
    font-size: 11pt;
    line-height: 1.45;
    padding: 2px 0 2px 12px;
  }
  table.cols td:first-child { padding: 2px 12px 2px 0; border-right: 1px solid #bbb; }
  table.cols p, table.cols ul, table.cols ol { margin: 0 0 6px 0; }
  table.cols ul, table.cols ol { padding-left: 1.25em; }
  .footer {
    margin-top: 28px;
    padding-top: 8px;
    border-top: 1px solid #999;
    color: #555;
    font-size: 9.5pt;
  }
</style>
</head>
<body>
<div class="meta">{{subject}} &middot; Writing Feedback &middot; {{date}}</div>
{{body}}
<div class="footer">This feedback was generated by an AI writing coach and reviewed under your teacher's direction. It is a starting point for revision &mdash; your own thinking comes first. Questions? Ask your teacher.</div>
</body>
</html>
""";
    }

    internal static void HtmlToPdf(string htmlPath, string pdfPath)
    {
        var browser = FindBrowser() ?? throw new InvalidOperationException(
            "No Chromium-based browser found for PDF conversion. On Windows, Microsoft Edge should be preinstalled.");

        var url = "file:///" + htmlPath.Replace('\\', '/');
        // A dedicated profile directory is essential: without it, when the
        // browser is already open the new msedge/chrome process just
        // forwards the request to the running instance and exits 0
        // WITHOUT printing.
        var profileDir = Path.Combine(Path.GetTempPath(), "writers-kiosk-headless-profile");
        var psi = new ProcessStartInfo(browser)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
        {
            "--headless", "--disable-gpu", "--no-first-run",
            "--no-default-browser-check", "--disable-extensions",
            $"--user-data-dir={profileDir}", "--no-pdf-header-footer",
            $"--print-to-pdf={pdfPath}", url,
        })
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {browser} for PDF conversion");
        proc.WaitForExit(60_000);

        // Some browser builds hand the job to a helper process and return
        // early; wait briefly for the PDF and for its size to settle.
        var waited = 0;
        while (!File.Exists(pdfPath) && waited < 15_000) { Thread.Sleep(250); waited += 250; }
        if (File.Exists(pdfPath))
        {
            long lastLen = 0;
            while (waited < 20_000)
            {
                Thread.Sleep(300);
                waited += 300;
                var len = new FileInfo(pdfPath).Length;
                if (len > 0 && len == lastLen) return;
                lastLen = len;
            }
            return;
        }
        throw new InvalidOperationException(
            "PDF conversion produced no file. If this persists, close all open browser windows once and retry, or update Microsoft Edge.");
    }

    private static string? FindBrowser()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Maps the kiosk's PRINT_DUPLEX value to the driver
    /// setting: "long" = book-style long-edge flip, "short" = short
    /// edge.</summary>
    internal static System.Drawing.Printing.Duplex ToDuplex(string duplex) =>
        duplex == "short"
            ? System.Drawing.Printing.Duplex.Horizontal
            : System.Drawing.Printing.Duplex.Vertical;

    /// <summary>True for "printers" that create a file instead of paper
    /// (they pop a save dialog — the exact opposite of a kiosk).</summary>
    internal static bool IsVirtualPrinter(string name) =>
        new[] { "Microsoft Print to PDF", "Microsoft XPS", "OneNote", "Adobe PDF", "Fax" }
            .Any(v => name.Contains(v, StringComparison.OrdinalIgnoreCase));

    private static string? DefaultPrinterName()
    {
        try { return new System.Drawing.Printing.PrinterSettings().PrinterName; }
        catch { return null; }
    }

    private static void DispatchToPrinter(string pdfPath, KioskConfig cfg)
    {
        // A file-making "printer" (Microsoft Print to PDF, Adobe PDF,
        // XPS, OneNote, Fax) would open a save dialog at the station no
        // matter which path spools the job — refuse up front with the
        // fix spelled out, instead of stranding a student at a dialog.
        var target = cfg.PrinterName ?? DefaultPrinterName();
        if (target is not null && IsVirtualPrinter(target))
            throw new InvalidOperationException(
                $"The target printer \"{target}\" creates a file instead of printing on paper, which opens a save dialog at the kiosk. " +
                "Make the classroom printer the Windows default (Settings > Bluetooth & devices > Printers & scanners), or set PRINTER_NAME in .env.");

        // Preferred: SumatraPDF prints fully silently and blocks until the
        // job is spooled, so the temp file can be deleted right after.
        if (FindSumatra(cfg.SumatraPath) is { } sumatra)
        {
            var psi = new ProcessStartInfo(sumatra) { UseShellExecute = false, CreateNoWindow = true };
            if (cfg.PrinterName is { } printer)
            {
                psi.ArgumentList.Add("-print-to");
                psi.ArgumentList.Add(printer);
            }
            else
            {
                psi.ArgumentList.Add("-print-to-default");
            }
            if (cfg.Duplex is { } duplex)
            {
                psi.ArgumentList.Add("-print-settings");
                psi.ArgumentList.Add(duplex == "short" ? "duplexshort" : "duplexlong");
            }
            psi.ArgumentList.Add("-silent");
            psi.ArgumentList.Add("-exit-when-done");
            psi.ArgumentList.Add(pdfPath);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to launch SumatraPDF at {sumatra}");
            proc.WaitForExit(120_000);
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"SumatraPDF reported a print failure (exit {proc.ExitCode})");
            KioskLog.Info("Printed via SumatraPDF.");
            return;
        }

        // No Sumatra: print with the in-box Windows PDF engine — fully
        // silent, no helper app, present on every Windows 10/11 device.
        try
        {
            PdfRasterPrinter.Print(pdfPath, cfg.PrinterName, cfg.Duplex);
            KioskLog.Info("Printed via the built-in Windows PDF engine (no helper app needed).");
            return;
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"Built-in PDF printing failed ({ex.Message}) — handing the file to the system's PDF app as a last resort. " +
                "That app may open a window (e.g. Adobe Acrobat) and need clicks; installing SumatraPDF restores fully silent printing.");
        }

        // Last resort: hand the PDF to the default print handler via the
        // Windows shell "print" verb. Wait so the temp file is not deleted
        // out from under the handler.
        var verb = new ProcessStartInfo(pdfPath) { UseShellExecute = true, Verb = "print", CreateNoWindow = true };
        using var handler = Process.Start(verb);
        handler?.WaitForExit(60_000);
        Thread.Sleep(8000);
    }

    private static string? FindSumatra(string? configured)
    {
        if (configured is not null && File.Exists(configured)) return configured;
        var candidates = new List<string>
        {
            @"C:\Program Files\SumatraPDF\SumatraPDF.exe",
            @"C:\Program Files (x86)\SumatraPDF\SumatraPDF.exe",
        };
        if (Environment.GetEnvironmentVariable("LOCALAPPDATA") is { } local)
            candidates.Add(Path.Combine(local, @"SumatraPDF\SumatraPDF.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
}
