// Writer's Kiosk tests — the in-box PDF print rung. GPL-3.0-or-later.
using System.Drawing.Printing;
using System.Text;
using Xunit;

namespace WritersKiosk.Tests;

/// <summary>
/// The middle rung of the print ladder is the one a district device
/// without SumatraPDF will actually use. The rasteriser is exercised on
/// real PDFs built in-test; the spooler leg runs through the real
/// print-to-file driver when the machine has one, and reports a skip —
/// not a pass — when it does not.
/// </summary>
public sealed class PdfRasterPrinterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kiosk-pdf-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public async Task LetterPagesRasterizeAt300Dpi()
    {
        // Pins two facts the code depends on: PdfPage.Size is in 96-per-inch
        // units (not points), and Windows.Data.Pdf scales the requested size
        // by the display's DPI factor — so 300 dpi must come out as 300 dpi
        // on a 100 % runner and on a 150 % laptop alike.
        var pdf = Write("letter.pdf", TinyPdf.Build(pages: 2, widthPt: 612, heightPt: 792));
        var pages = await PdfRasterPrinter.RasterizeAsync(pdf);
        try
        {
            Assert.Equal(2, pages.Count);
            Assert.All(pages, page =>
            {
                Assert.InRange(page.Width, 2549, 2551);  // 8.5 in × 300, ±1 for the renderer's rounding
                Assert.InRange(page.Height, 3299, 3301); // 11 in × 300
            });
        }
        finally { foreach (var page in pages) page.Dispose(); }
    }

    [Fact]
    public async Task AnAbsurdPageSizeIsClampedNotHonoured()
    {
        // 200 in square — the largest MediaBox the PDF spec allows.
        var pdf = Write("huge.pdf", TinyPdf.Build(pages: 1, widthPt: 14400, heightPt: 14400));
        var pages = await PdfRasterPrinter.RasterizeAsync(pdf);
        try
        {
            var page = Assert.Single(pages);
            // The ceiling is a bound the renderer must not exceed, whatever
            // the display's scale factor; rounding may land a pixel under.
            Assert.InRange(page.Width, 4398, 4400);
            Assert.InRange(page.Height, 5698, 5700);
        }
        finally { foreach (var page in pages) page.Dispose(); }
    }

    [Fact]
    public async Task APdfWithNoPagesIsRefused()
    {
        var pdf = Write("empty.pdf", TinyPdf.Build(pages: 0, widthPt: 612, heightPt: 792));
        // Windows.Data.Pdf refuses to load a document with no pages
        // before our own "no pages to print" check can run; either way
        // nothing reaches the spooler, which is the behaviour that matters.
        await Assert.ThrowsAnyAsync<Exception>(() => PdfRasterPrinter.RasterizeAsync(pdf));
    }

    [SkippableFact]
    public async Task PrintsToFileThroughTheRealSpooler()
    {
        const string printer = "Microsoft Print to PDF";
        Skip.IfNot(
            PrinterSettings.InstalledPrinters.Cast<string>().Contains(printer),
            $"\"{printer}\" is not installed on this machine, so the spooler leg cannot run here.");

        var source = Write("report.pdf", TinyPdf.Build(pages: 2, widthPt: 612, heightPt: 792));
        var output = Path.Combine(_dir, "spooled.pdf");
        var settings = new PrinterSettings { PrinterName = printer, PrintToFile = true, PrintFileName = output };

        PdfRasterPrinter.Print(source, settings, duplex: "long");

        var bytes = WaitForFile(output);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        // Round trip: what the spooler produced is itself a two-page PDF.
        var again = await PdfRasterPrinter.RasterizeAsync(output);
        try { Assert.Equal(2, again.Count); }
        finally { foreach (var page in again) page.Dispose(); }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>The print-to-file driver finishes writing after
    /// <c>Print()</c> returns; wait until the file exists and is no
    /// longer held open.</summary>
    private static byte[] WaitForFile(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    if (stream.Length > 0)
                    {
                        var bytes = new byte[stream.Length];
                        stream.ReadExactly(bytes);
                        return bytes;
                    }
                }
            }
            catch (IOException) { /* still being written */ }
            Thread.Sleep(250);
        }
        throw new TimeoutException($"The spooler never finished writing {path}.");
    }
}

/// <summary>
/// A minimal, valid PDF built by hand: N pages of one size, each with a
/// filled rectangle so a rasterised page is not blank. ASCII only, so
/// character offsets are byte offsets for the cross-reference table.
/// </summary>
internal static class TinyPdf
{
    public static byte[] Build(int pages, int widthPt, int heightPt)
    {
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        void Obj(string body)
        {
            offsets.Add(sb.Length);
            sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        Obj("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, pages).Select(i => $"{3 + 2 * i} 0 R"));
        Obj($"<< /Type /Pages /Kids [{kids}] /Count {pages} >>");
        for (var i = 0; i < pages; i++)
        {
            var contentNo = 4 + 2 * i;
            Obj($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {widthPt} {heightPt}] /Contents {contentNo} 0 R >>");
            var drawing = $"0 0 0 rg 36 36 {Math.Max(1, widthPt / 4)} {Math.Max(1, heightPt / 8)} re f";
            Obj($"<< /Length {drawing.Length} >>\nstream\n{drawing}\nendstream");
        }

        var xref = sb.Length;
        sb.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets) sb.Append(offset.ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\n")
          .Append("startxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
