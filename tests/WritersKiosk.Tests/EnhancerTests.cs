// Writer's Kiosk tests — image-enhancement safety. GPL-3.0-or-later.
using System.Drawing.Imaging;
using Xunit;

namespace WritersKiosk.Tests;

public sealed class EnhancerTests
{
    private static Bitmap Uniform(Color color)
    {
        var bmp = new Bitmap(64, 64, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    [Fact]
    public void ABrightBlankPageStaysBright()
    {
        // A blank worksheet filling the whole frame: its darkest pixels
        // are still near-white. The contrast stretch must not adopt that
        // as the black point and crush the page toward black.
        using var bmp = Uniform(Color.FromArgb(220, 220, 220));
        new Enhancer(enabled: true).Process(bmp);
        var px = bmp.GetPixel(32, 32);
        Assert.True(px.R > 150, $"blank page went dark: {px}");
    }

    [Fact]
    public void ACoveredLensStaysDarkInsteadOfAmplifyingNoise()
    {
        using var bmp = Uniform(Color.FromArgb(10, 10, 10));
        new Enhancer(enabled: true).Process(bmp);
        var px = bmp.GetPixel(32, 32);
        Assert.True(px.R < 40, $"near-black frame was over-brightened: {px}");
    }

    [Fact]
    public void DisabledEnhancerLeavesPixelsUntouched()
    {
        using var bmp = Uniform(Color.FromArgb(123, 45, 67));
        new Enhancer(enabled: false).Process(bmp);
        var px = bmp.GetPixel(10, 10);
        Assert.Equal(Color.FromArgb(123, 45, 67).ToArgb(), px.ToArgb());
    }
}
