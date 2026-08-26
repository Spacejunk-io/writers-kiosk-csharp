// Writer's Kiosk (C#) — preview window, capture loop, worker thread.
// GPL-3.0-or-later; see LICENSE.
using FlashCap;

namespace WritersKiosk;

public sealed class KioskForm : Form
{
    private const string TitleReady =
        "Writer's Kiosk — SPACE: get feedback · N: add page · C: camera · V/H: flip";
    private const string TitleBusy =
        "Writer's Kiosk — reading your work… feedback will print shortly";
    private const int MaxPages = 4;

    private readonly KioskConfig _cfg;
    private readonly PictureBox _view = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly Enhancer _enhancer;
    private readonly object _stateLock = new();

    private IReadOnlyList<CaptureDeviceDescriptor> _descriptors = [];
    private CaptureDevice? _device;
    private int _camIndex;
    private bool _switching;

    private Bitmap? _currentFrame; // post flip+enhance, pre-overlay (for capture)
    private bool _sized;

    private bool _busy;
    private bool _flipV, _flipH;
    private readonly List<byte[]> _batch = [];
    private string[]? _noticeLines;
    private DateTime _noticeUntil;
    private DateTime _submitOkAt = DateTime.MinValue;
    private DateTime _captureOkAt = DateTime.MinValue;

    public KioskForm(KioskConfig cfg)
    {
        _cfg = cfg;
        _enhancer = new Enhancer(cfg.Enhance);
        _flipV = cfg.FlipVertical;
        _flipH = cfg.FlipHorizontal;

        Text = TitleReady;
        KeyPreview = true;
        ClientSize = new Size(960, 540);
        Controls.Add(_view);
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico")); } catch { }

        if (cfg.WindowPos is { } pos)
        {
            StartPosition = FormStartPosition.Manual;
            Location = pos;
        }
        if (cfg.AssignmentContext is not null)
            Console.WriteLine("[kiosk] Loaded teacher assignment context from assignment.txt.");
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            _descriptors = new CaptureDevices().EnumerateDescriptors().ToArray();
            if (_descriptors.Count == 0)
                throw new InvalidOperationException("No cameras detected. Is the document camera plugged in?");

            Console.WriteLine("[kiosk] Cameras detected (press C in the kiosk window to switch):");
            for (var i = 0; i < _descriptors.Count; i++)
                Console.WriteLine($"[kiosk]   {i}: {_descriptors[i].Name}");

            _camIndex = Math.Min(_cfg.CameraIndex, _descriptors.Count - 1);
            Console.WriteLine($"[kiosk] Opening camera {_camIndex}…");
            await OpenCameraAsync(_camIndex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[kiosk] Startup error: {ex.Message}");
            MessageBox.Show(ex.Message, "Writer's Kiosk", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task OpenCameraAsync(int index)
    {
        if (_device is not null)
        {
            await _device.StopAsync();
            _device.Dispose();
            _device = null;
        }
        var descriptor = _descriptors[index];
        // Highest available resolution — document cameras need every pixel
        // for handwriting legibility.
        var characteristics = descriptor.Characteristics
            .Where(c => c.PixelFormat != PixelFormats.Unknown)
            .OrderByDescending(c => (long)c.Width * c.Height)
            .First();
        _device = await descriptor.OpenAsync(characteristics, OnFrame);
        await _device.StartAsync();
        Console.WriteLine($"[kiosk] Camera streaming at {characteristics.Width}x{characteristics.Height}.");
    }

    /// <summary>Runs on FlashCap's capture thread for every frame.</summary>
    private void OnFrame(PixelBufferScope scope)
    {
        byte[] imageBytes;
        try { imageBytes = scope.Buffer.ExtractImage(); }
        finally { scope.ReleaseNow(); }

        Bitmap frame;
        using (var ms = new MemoryStream(imageBytes))
        using (var decoded = new Bitmap(ms))
        {
            // Normalize to 24bpp so the enhancer's pixel walk is uniform.
            frame = new Bitmap(decoded.Width, decoded.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(frame);
            g.DrawImage(decoded, 0, 0, decoded.Width, decoded.Height);
        }

        bool busy;
        string[]? notice;
        lock (_stateLock)
        {
            // Orientation correction applies to the frame itself, so both
            // the preview and the captured images are upright.
            if (_flipV) frame.RotateFlip(RotateFlipType.RotateNoneFlipY);
            if (_flipH) frame.RotateFlip(RotateFlipType.RotateNoneFlipX);
            _enhancer.Process(frame);

            _currentFrame?.Dispose();
            _currentFrame = frame;
            busy = _busy;
            notice = DateTime.Now < _noticeUntil ? _noticeLines : null;
        }

        // Build the display copy (overlays never touch the capture frame).
        var display = (Bitmap)frame.Clone();
        if (busy) DimForBusy(display);
        if (notice is not null) DrawNotice(display, notice);

        try
        {
            BeginInvoke(() =>
            {
                if (!_sized)
                {
                    _sized = true;
                    var w = Math.Min(display.Width, 1100);
                    ClientSize = new Size(w, Math.Max(1, w * display.Height / display.Width));
                }
                var old = _view.Image;
                _view.Image = display;
                old?.Dispose();
            });
        }
        catch (ObjectDisposedException)
        {
            display.Dispose(); // form closing; drop the frame
        }
    }

    private static void DimForBusy(Bitmap bmp)
    {
        using var g = Graphics.FromImage(bmp);
        g.FillRectangle(new SolidBrush(Color.FromArgb(128, 0, 0, 0)), 0, 0, bmp.Width, bmp.Height);
    }

    /// <summary>
    /// Calm centered notice: ~90% opacity panel over the middle of the
    /// preview, static, no animation — video stays visible around it.
    /// </summary>
    private static void DrawNotice(Bitmap bmp, string[] lines)
    {
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var fontSize = Math.Clamp(bmp.Height / 22f, 12f, 32f);
        using var font = new Font("Segoe UI", fontSize, GraphicsUnit.Pixel);
        var panelW = (int)(bmp.Width * 0.72);
        var text = string.Join("\n", lines);
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        var textSize = g.MeasureString(text, font, panelW - (int)(fontSize * 2));
        var pad = (int)(fontSize * 1.1);
        var panelH = (int)textSize.Height + pad * 2;
        var x = (bmp.Width - panelW) / 2;
        var y = (bmp.Height - panelH) / 2;

        g.FillRectangle(new SolidBrush(Color.FromArgb(230, 35, 39, 51)), x, y, panelW, panelH);
        g.DrawRectangle(new Pen(Color.FromArgb(0x8B, 0x5C, 0xE8), 3), x, y, panelW, panelH);
        var textRect = new RectangleF(x + pad, y + pad, panelW - pad * 2, panelH - pad * 2);
        g.DrawString(text, font, new SolidBrush(Color.FromArgb(0xF2, 0xF2, 0xF5)), textRect, format);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Escape:
                Close();
                break;

            case Keys.V:
                lock (_stateLock) _flipV = !_flipV;
                Console.WriteLine($"[kiosk] Vertical flip {(_flipV ? "on" : "off")}.");
                break;

            case Keys.H:
                lock (_stateLock) _flipH = !_flipH;
                Console.WriteLine($"[kiosk] Horizontal flip {(_flipH ? "on" : "off")}.");
                break;

            case Keys.E:
                _enhancer.Enabled = !_enhancer.Enabled;
                Console.WriteLine($"[kiosk] Auto image enhancement {(_enhancer.Enabled ? "on" : "off")}.");
                break;

            case Keys.C when !_busy && !_switching && _descriptors.Count > 1:
                _switching = true;
                var next = (_camIndex + 1) % _descriptors.Count;
                try
                {
                    await OpenCameraAsync(next);
                    _camIndex = next;
                    Console.WriteLine($"[kiosk] Switched to camera {next}: {_descriptors[next].Name}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[kiosk] Could not switch to camera {next}: {ex.Message}");
                }
                finally { _switching = false; }
                break;

            case Keys.N when !_busy:
                DismissNotice();
                HandleAddPage();
                break;

            case Keys.Space when !_busy:
                DismissNotice();
                HandleSubmit();
                break;
        }
    }

    private void DismissNotice()
    {
        lock (_stateLock) _noticeUntil = DateTime.MinValue;
    }

    private static int SecondsLeft(DateTime at) =>
        Math.Max(1, (int)Math.Ceiling((at - DateTime.Now).TotalSeconds));

    private Bitmap? SnapshotFrame()
    {
        lock (_stateLock)
            return _currentFrame is null ? null : (Bitmap)_currentFrame.Clone();
    }

    private void HandleAddPage()
    {
        if (DateTime.Now < _captureOkAt)
        {
            Console.WriteLine($"[kiosk] One moment — page just captured. Try again in {SecondsLeft(_captureOkAt)}s.");
            return;
        }
        if (_batch.Count >= MaxPages - 1)
        {
            Console.WriteLine($"[kiosk] Page limit ({MaxPages}) nearly reached — press SPACE to capture the last page and get feedback.");
            return;
        }
        using var frame = SnapshotFrame();
        if (frame is null) return;
        _batch.Add(ImageOps.EncodeJpeg(frame));
        _captureOkAt = DateTime.Now.AddSeconds(1.5);
        Console.WriteLine($"[kiosk] Page {_batch.Count} captured. Place the next page, then N again or SPACE to finish.");
        Text = $"Writer's Kiosk — {_batch.Count} page(s) captured · SPACE: capture last page & get feedback";
    }

    private void HandleSubmit()
    {
        if (DateTime.Now < _submitOkAt)
        {
            Console.WriteLine($"[kiosk] Cooldown — next submission in {SecondsLeft(_submitOkAt)}s (prevents accidental repeat API calls).");
            return;
        }
        if (DateTime.Now < _captureOkAt)
        {
            Console.WriteLine($"[kiosk] One moment — page just captured. Try again in {SecondsLeft(_captureOkAt)}s.");
            return;
        }
        using var frame = SnapshotFrame();
        if (frame is null) return;

        // Capture: the page images exist only as these in-memory JPEG
        // buffers. They are never written to disk and are released as
        // soon as the API call finishes.
        var pages = new List<byte[]>(_batch) { ImageOps.EncodeJpeg(frame) };
        _batch.Clear();
        var kb = pages.Sum(p => p.Length) / 1024;
        Console.WriteLine($"[kiosk] Submitting {pages.Count} page(s) ({kb} KB). Requesting feedback…");

        lock (_stateLock) _busy = true;
        Text = TitleBusy;

        Task.Run(async () =>
        {
            string[]? notice = null;
            string? error = null;
            try
            {
                var markdown = await LlmClient.GetFeedbackAsync(_cfg, pages);
                pages.Clear(); // image bytes released before printing
                notice = LlmClient.NoticeFor(markdown);
                if (notice is null)
                {
                    Console.WriteLine("[kiosk] Feedback received. Printing…");
                    Printing.PrintMarkdown(markdown, _cfg);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            try
            {
                BeginInvoke(() =>
                {
                    lock (_stateLock)
                    {
                        _busy = false;
                        if (notice is not null)
                        {
                            _noticeLines = notice;
                            _noticeUntil = DateTime.Now.AddSeconds(9);
                        }
                    }
                    _submitOkAt = DateTime.Now.AddSeconds(_cfg.CooldownSeconds);
                    Text = TitleReady;
                    if (error is not null)
                        Console.Error.WriteLine($"[kiosk] This capture failed: {error}\n[kiosk] Ready to try again.");
                    else if (notice is not null)
                        Console.WriteLine($"[kiosk] Not printed: {string.Join(" ", notice)}");
                    else
                        Console.WriteLine("[kiosk] Report sent to the printer. Ready for the next student.");
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_device is not null)
        {
            var device = _device;
            _device = null;
            try { await device.StopAsync(); device.Dispose(); } catch { }
        }
        Console.WriteLine("[kiosk] Goodbye.");
    }
}
