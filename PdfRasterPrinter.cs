// Writer's Kiosk (C#) — silent PDF printing with in-box Windows parts.
// GPL-3.0-or-later; see LICENSE.
//
// The middle rung of the printing ladder: when SumatraPDF is not
// installed (it prints vector and stays the preferred helper), this
// path prints the report using ONLY components every Windows 10/11
// device already has — Windows.Data.Pdf rasterizes each page, and
// System.Drawing.Printing spools the bitmaps straight to the printer.
// No window, no dialog, no clicks, no third-party install: exactly
// what a district-managed device fleet needs. The old hand-off to the
// system's default PDF app (which can open Acrobat in front of
// students) remains only as a last resort behind this.
using System.Drawing.Printing;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WritersKiosk;

internal static class PdfRasterPrinter
{
    /// <summary>Pages render at this density — crisp for 12.5pt report
    /// text without ballooning the spool file.</summary>
    private const double RenderDpi = 300.0;

    /// <summary>Prints the PDF silently to the named printer (null =
    /// system default). <paramref name="duplex"/> is the kiosk's
    /// "long"/"short" setting, applied when the driver supports it.</summary>
    public static void Print(string pdfPath, string? printerName, string? duplex)
    {
        var settings = new PrinterSettings();
        if (printerName is not null) settings.PrinterName = printerName;
        if (!settings.IsValid)
            throw new InvalidOperationException(
                $"Printer \"{settings.PrinterName}\" is not available on this machine.");
        Print(pdfPath, settings, duplex);
    }

    /// <summary>Testable core: prints to caller-supplied settings (which
    /// may target print-to-file for hardware-free verification).</summary>
    internal static void Print(string pdfPath, PrinterSettings settings, string? duplex)
    {
        var pages = RasterizeAsync(pdfPath).GetAwaiter().GetResult();
        try
        {
            PrintBitmaps(pages, settings, duplex);
        }
        finally
        {
            foreach (var page in pages) page.Dispose();
        }
    }

    private static async Task<List<Bitmap>> RasterizeAsync(string pdfPath)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
        var document = await PdfDocument.LoadFromFileAsync(file);
        var pages = new List<Bitmap>();
        try
        {
            for (uint i = 0; i < document.PageCount; i++)
            {
                using var page = document.GetPage(i);
                // Page size is in points (1/72"); clamp so a malformed
                // page can never demand an absurd bitmap.
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Clamp(page.Size.Width * RenderDpi / 72.0, 1, 4400),
                    DestinationHeight = (uint)Math.Clamp(page.Size.Height * RenderDpi / 72.0, 1, 5700),
                };
                using var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, options);
                using var netStream = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await netStream.CopyToAsync(ms);
                ms.Position = 0;
                // Copy out of the stream-backed bitmap: GDI+ requires
                // the source stream to outlive it otherwise.
                using var streamBacked = new Bitmap(ms);
                pages.Add(new Bitmap(streamBacked));
            }
        }
        catch
        {
            foreach (var page in pages) page.Dispose();
            throw;
        }
        if (pages.Count == 0)
            throw new InvalidOperationException("The PDF contained no pages to print.");
        return pages;
    }

    private static void PrintBitmaps(List<Bitmap> pages, PrinterSettings settings, string? duplex)
    {
        using var document = new PrintDocument();
        document.PrinterSettings = settings;
        document.DocumentName = "Writer's Kiosk feedback report";
        // StandardPrintController suppresses the on-screen progress box.
        document.PrintController = new StandardPrintController();
        if (duplex is not null && settings.CanDuplex)
            settings.Duplex = Printing.ToDuplex(duplex);
        document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        var index = 0;
        document.PrintPage += (_, e) =>
        {
            var bitmap = pages[index];
            // Aspect-fit onto the page (units are 1/100 inch). The
            // report already carries its own margins, so a full-page
            // fit re-creates the PDF's layout exactly.
            var bounds = e.PageBounds;
            var scale = Math.Min((float)bounds.Width / bitmap.Width, (float)bounds.Height / bitmap.Height);
            var w = bitmap.Width * scale;
            var h = bitmap.Height * scale;
            e.Graphics!.DrawImage(bitmap, (bounds.Width - w) / 2f, (bounds.Height - h) / 2f, w, h);
            index++;
            e.HasMorePages = index < pages.Count;
        };
        document.Print();
    }
}
