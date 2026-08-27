// Writer's Kiosk (C#) — image conditioning. GPL-3.0-or-later; see LICENSE.
using System.Drawing.Imaging;

namespace WritersKiosk;

/// <summary>
/// Software image conditioning: gray-world white balance plus a gentle
/// contrast/exposure stretch. Compensates for cameras stuck with a dark
/// or color-tinted output (e.g. a purple cast in low light). Correction
/// parameters are re-measured periodically; application is three lookup
/// tables, cheap enough to run on every preview frame so the display
/// matches what the AI receives.
/// </summary>
public sealed class Enhancer
{
    public bool Enabled { get; set; }
    private byte[][]? _luts; // [channel B,G,R][value]
    private int _framesSinceMeasure;

    public Enhancer(bool enabled) => Enabled = enabled;

    public unsafe void Process(Bitmap bmp)
    {
        if (!Enabled) return;

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            if (_luts is null || _framesSinceMeasure >= 15)
            {
                _luts = Measure(data);
                _framesSinceMeasure = 0;
            }
            _framesSinceMeasure++;

            var luts = _luts;
            var p = (byte*)data.Scan0;
            for (var y = 0; y < data.Height; y++)
            {
                var row = p + y * data.Stride;
                var rowEnd = row + data.Width * 3;
                for (var px = row; px < rowEnd; px += 3)
                {
                    px[0] = luts[0][px[0]];
                    px[1] = luts[1][px[1]];
                    px[2] = luts[2][px[2]];
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static unsafe byte[][] Measure(BitmapData data)
    {
        long sumB = 0, sumG = 0, sumR = 0, count = 0;
        var hist = new long[256];
        var p = (byte*)data.Scan0;
        // Sample every 8th pixel — plenty for global statistics.
        for (var y = 0; y < data.Height; y += 2)
        {
            var row = p + y * data.Stride;
            for (var x = 0; x < data.Width; x += 4)
            {
                var px = row + x * 3;
                long b = px[0], g = px[1], r = px[2];
                sumB += b; sumG += g; sumR += r;
                hist[(r * 299 + g * 587 + b * 114) / 1000]++;
                count++;
            }
        }

        var identity = new byte[3][];
        for (var c = 0; c < 3; c++)
        {
            identity[c] = new byte[256];
            for (var v = 0; v < 256; v++) identity[c][v] = (byte)v;
        }
        if (count < 100) return identity;

        // Gray-world white balance: scale each channel toward the common mean.
        float mb = (float)sumB / count, mg = (float)sumG / count, mr = (float)sumR / count;
        var mean = (mb + mg + mr) / 3f;
        static float Gain(float m, float mean) => m < 1f ? 1f : Math.Clamp(mean / m, 0.7f, 1.4f);
        float[] gains = [Gain(mb, mean), Gain(mg, mean), Gain(mr, mean)];

        // Exposure/contrast: stretch the 2nd–98th luminance percentiles.
        long cum = 0, pLow = count * 2 / 100, pHigh = count * 98 / 100;
        int low = 0, high = 255;
        for (var v = 0; v < 256; v++)
        {
            cum += hist[v];
            if (cum <= pLow) low = v;
            if (cum <= pHigh) high = v;
        }
        // A frame that is almost entirely bright paper (a blank page
        // filling the camera) has its 2nd percentile up near white;
        // using that as the black point would crush the whole page
        // toward black. Cap the black anchor so bright pages stay
        // bright and only genuinely dark regions stretch down.
        low = Math.Min(low, 64);
        // Near-flat image (e.g. lens covered): don't amplify noise.
        var stretch = high > low + 24 ? Math.Min(235f / (high - low), 2.2f) : 1f;

        var luts = new byte[3][];
        for (var c = 0; c < 3; c++)
        {
            luts[c] = new byte[256];
            for (var v = 0; v < 256; v++)
            {
                var val = (v * gains[c] - low) * stretch + 8f;
                luts[c][v] = (byte)Math.Clamp(val, 0f, 255f);
            }
        }
        return luts;
    }
}

public static class ImageOps
{
    /// <summary>Encodes a frame as JPEG bytes in memory for the API request.</summary>
    public static byte[] EncodeJpeg(Bitmap bmp, long quality = 90)
    {
        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parms = new EncoderParameters(1);
        parms.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        using var ms = new MemoryStream();
        bmp.Save(ms, encoder, parms);
        return ms.ToArray();
    }
}
