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
    private readonly EntraTokenProvider? _entra;
    private readonly SessionSettings _session;
    private ToolStripMenuItem _middleMenu = null!;
    private ToolStripMenuItem _highMenu = null!;
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
    private (string Markdown, string Subject)? _lastReport;
    private DateTime _lastFrameAt = DateTime.MaxValue;
    private DateTime _lastFrameErrorAt = DateTime.MinValue;
    private bool _cameraLost;
    private readonly System.Windows.Forms.Timer _watchdog = new() { Interval = 1000 };
    private readonly List<byte[]> _batch = [];
    private string[]? _noticeLines;
    private DateTime _noticeUntil;
    private DateTime _submitOkAt = DateTime.MinValue;
    private DateTime _captureOkAt = DateTime.MinValue;

    public KioskForm(KioskConfig cfg)
    {
        _cfg = cfg;
        _session = SessionSettings.Load(cfg);
        if (cfg.Provider == Provider.Azure && cfg.AzureUseEntra)
            _entra = new EntraTokenProvider(cfg.AzureTenantId, cfg.AzureClientId);
        _enhancer = new Enhancer(cfg.Enhance);
        _flipV = cfg.FlipVertical;
        _flipH = cfg.FlipHorizontal;

        Text = TitleReady;
        KeyPreview = true;
        ClientSize = new Size(960, 540);
        Controls.Add(_view);
        try { Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "icon.ico")); } catch { }

        BuildMenu();

        if (cfg.WindowPos is { } pos)
        {
            StartPosition = FormStartPosition.Manual;
            Location = pos;
        }
        if (cfg.AssignmentContext is not null)
            Console.WriteLine("[kiosk] Loaded teacher assignment context from assignment.txt.");
        Console.WriteLine($"[kiosk] Feedback tuned for: {_session.LevelPhrase} {_session.Subject} (change via the menu bar).");
    }

    // ── Menu bar: Assignment · Middle School · High School · Help ─────

    private void BuildMenu()
    {
        var menu = new MenuStrip();

        var assignment = new ToolStripMenuItem("Assignment");
        assignment.DropDownItems.Add(new ToolStripMenuItem(
            "Edit Today's Assignment…", null, (_, _) => EditAssignment()));

        _middleMenu = new ToolStripMenuItem("Middle School");
        foreach (var subject in Subjects.MiddleSubjects)
            _middleMenu.DropDownItems.Add(new ToolStripMenuItem(
                subject, null, (_, _) => SelectSubject(Subjects.Middle, subject)));

        _highMenu = new ToolStripMenuItem("High School");
        foreach (var subject in Subjects.HighSubjects)
            _highMenu.DropDownItems.Add(new ToolStripMenuItem(
                subject, null, (_, _) => SelectSubject(Subjects.High, subject)));

        var reports = new ToolStripMenuItem("Reports");
        reports.DropDownItems.Add(new ToolStripMenuItem(
            "Reprint Last Report  (R)", null, (_, _) => ReprintLast()));
        reports.DropDownItems.Add(new ToolStripMenuItem(
            "Open Feedback Log Folder", null, (_, _) => OpenLogFolder()));

        var help = new ToolStripMenuItem("Help && Support");
        help.DropDownItems.Add(new ToolStripMenuItem("Help", null, (_, _) => ShowHelp()));
        help.DropDownItems.Add(new ToolStripMenuItem("Hotkeys", null, (_, _) => ShowHotkeys()));
        help.DropDownItems.Add(new ToolStripMenuItem("About", null, (_, _) => ShowAbout()));

        menu.Items.AddRange([assignment, _middleMenu, _highMenu, reports, help]);
        MainMenuStrip = menu;
        Controls.Add(menu);
        _view.BringToFront();
        RefreshSubjectChecks();
    }

    /// <summary>Applies a level+subject choice immediately — no restart.</summary>
    private void SelectSubject(string level, string subject)
    {
        _session.Level = level;
        _session.Subject = subject;
        _session.SaveUiState();
        RefreshSubjectChecks();
        Console.WriteLine($"[kiosk] Feedback now tuned for: {_session.LevelPhrase} {subject}.");
    }

    private void RefreshSubjectChecks()
    {
        foreach (var (menu, level) in new[] { (_middleMenu, Subjects.Middle), (_highMenu, Subjects.High) })
        {
            var isActiveLevel = _session.Level == level;
            menu.Font = new Font(menu.Font ?? Font, isActiveLevel ? FontStyle.Bold : FontStyle.Regular);
            foreach (ToolStripMenuItem item in menu.DropDownItems)
                item.Checked = isActiveLevel && item.Text == _session.Subject;
        }
    }

    /// <summary>
    /// In-app assignment editing: takes effect on the very next capture
    /// and is saved back to assignment.txt so it survives restarts.
    /// </summary>
    private void EditAssignment()
    {
        using var dialog = new Form
        {
            Text = "Today's Assignment — a few plain sentences that focus the AI feedback",
            ClientSize = new Size(640, 400),
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowIcon = false,
        };
        var box = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f),
            Text = _session.AssignmentContext ?? "",
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8),
        };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        var clear = new Button { Text = "Clear", Width = 90 };
        clear.Click += (_, _) => box.Text = "";
        buttons.Controls.AddRange([save, cancel, clear]);
        dialog.Controls.Add(box);
        dialog.Controls.Add(buttons);
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var text = box.Text.Trim();
        _session.AssignmentContext = text.Length > 0 ? text : null;
        try
        {
            File.WriteAllText(_cfg.AssignmentFile, text);
            Console.WriteLine(text.Length > 0
                ? $"[kiosk] Assignment context updated (saved to {_cfg.AssignmentFile}); applies to the next capture."
                : "[kiosk] Assignment context cleared — feedback returns to general mode.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[kiosk] Assignment applied for this session, but saving failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reprints the most recent report — no new capture, no API cost.
    /// The printer-jam recovery path.
    /// </summary>
    private void ReprintLast()
    {
        (string Markdown, string Subject)? last;
        lock (_stateLock) last = _lastReport;
        if (last is null)
        {
            Console.WriteLine("[kiosk] No report to reprint yet this session.");
            return;
        }
        lock (_stateLock) _busy = true;
        Text = "Writer's Kiosk — reprinting the last report…";
        Task.Run(() =>
        {
            string? error = null;
            try { Printing.PrintMarkdown(last.Value.Markdown, _cfg, last.Value.Subject); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke(() =>
                {
                    lock (_stateLock) _busy = false;
                    Text = TitleReady;
                    Console.WriteLine(error is null
                        ? "[kiosk] Reprint sent to the printer."
                        : $"[kiosk] Reprint failed: {error}");
                });
            }
            catch (ObjectDisposedException) { }
        });
    }

    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(FeedbackLog.Folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.GetFullPath(FeedbackLog.Folder),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[kiosk] Could not open the log folder: {ex.Message}");
        }
    }

    private void ShowHelp() => MessageBox.Show(this,
        """
        Writer's Kiosk gives students printed AI feedback on their written classwork.

        1. Place the page under the document camera; check it is sharp and fills the preview.
        2. Press SPACE — the preview dims while the report is generated (10–25 seconds) and printing starts by itself.
        3. Multi-page work: press N on each earlier page, then SPACE on the last one.

        The menu bar sets the school level & subject the feedback is tuned for, and "Assignment" lets you describe today's task so the feedback focuses on it — both take effect immediately.

        Every report's text (never images, never names) is saved to the feedback-log folder for teacher review — see the Reports menu, which can also reprint the last report after a printer jam (or press R). If the camera cable is bumped loose, the title bar says so; reconnect it and press C.

        Off-topic or blank pages show a brief on-screen notice instead of printing. Keep student names off submitted pages (a cover strip at the station handles exceptions).
        """,
        "Help — Writer's Kiosk", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ShowHotkeys() => MessageBox.Show(this,
        """
        SPACE  — capture the page and print feedback (cooldown between submissions)
        N      — add this page to a multi-page submission (up to 4), then SPACE to finish
        C      — switch to the next connected camera
        V / H  — flip the image vertically / horizontally
        E      — toggle auto image enhancement (white balance + exposure)
        R      — reprint the last report (no new AI request; printer-jam recovery)
        ESC    — quit the kiosk
        """,
        "Hotkeys — Writer's Kiosk", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ShowAbout() => MessageBox.Show(this,
        $"""
        Writer's Kiosk (C# edition) v{Application.ProductVersion}

        A privacy-first classroom kiosk: student work is photographed in memory only (never saved), analyzed by an AI writing coach, and returned as a printed feedback report.

        Copyright (C) 2026 Spacejunk-IO — George Bacon
        Free software under the GNU GPL v3 or later.
        https://github.com/Spacejunk-io/writers-kiosk-csharp
        """,
        "About — Writer's Kiosk", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            // Warm up the district sign-in at launch, so the one-time
            // browser prompt (first run only) never interrupts a class.
            if (_entra is not null)
            {
                Console.WriteLine("[kiosk] Keyless Azure mode: verifying district sign-in…");
                await _entra.GetTokenAsync();
                Console.WriteLine("[kiosk] District sign-in OK — no API key in use.");
            }

            _descriptors = new CaptureDevices().EnumerateDescriptors().ToArray();
            if (_descriptors.Count == 0)
                throw new InvalidOperationException("No cameras detected. Is the document camera plugged in?");

            Console.WriteLine("[kiosk] Cameras detected (press C in the kiosk window to switch):");
            for (var i = 0; i < _descriptors.Count; i++)
                Console.WriteLine($"[kiosk]   {i}: {_descriptors[i].Name}");

            _camIndex = Math.Min(_cfg.CameraIndex, _descriptors.Count - 1);
            Console.WriteLine($"[kiosk] Opening camera {_camIndex}…");
            await OpenCameraAsync(_camIndex);
            _watchdog.Tick += OnWatchdogTick;
            _watchdog.Start();
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
        _lastFrameAt = DateTime.Now;
        Console.WriteLine($"[kiosk] Camera streaming at {characteristics.Width}x{characteristics.Height}.");
    }

    /// <summary>
    /// Watches for a camera that has stopped delivering frames (cable
    /// bumped loose, device sleep) and says so instead of crashing.
    /// </summary>
    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (_device is null || _switching || _lastFrameAt == DateTime.MaxValue) return;
        var stale = DateTime.Now - _lastFrameAt > TimeSpan.FromSeconds(3);
        if (stale && !_cameraLost)
        {
            _cameraLost = true;
            Text = "Writer's Kiosk — camera signal lost · check the cable, then press C to reconnect";
            Console.Error.WriteLine("[kiosk] Camera signal lost — check the USB cable, then press C to reconnect.");
        }
        else if (!stale && _cameraLost)
        {
            _cameraLost = false;
            if (!_busy) Text = TitleReady;
            Console.WriteLine("[kiosk] Camera signal restored.");
        }
    }

    /// <summary>Runs on FlashCap's capture thread for every frame.</summary>
    private void OnFrame(PixelBufferScope scope)
    {
        // A bad frame (device yanked mid-transfer, decoder hiccup) must
        // never take the kiosk down — drop it and keep streaming.
        try
        {
            OnFrameCore(scope);
        }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (DateTime.Now - _lastFrameErrorAt > TimeSpan.FromSeconds(5))
            {
                _lastFrameErrorAt = DateTime.Now;
                Console.Error.WriteLine($"[kiosk] Camera frame error: {ex.Message}");
            }
        }
    }

    private void OnFrameCore(PixelBufferScope scope)
    {
        byte[] imageBytes;
        try { imageBytes = scope.Buffer.ExtractImage(); }
        finally { scope.ReleaseNow(); }
        _lastFrameAt = DateTime.Now;

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

            case Keys.C when !_busy && !_switching:
                // C both switches cameras and recovers a lost one: the
                // device list is re-enumerated fresh on every press.
                _switching = true;
                try
                {
                    var found = new CaptureDevices().EnumerateDescriptors().ToArray();
                    if (found.Length == 0)
                    {
                        Console.Error.WriteLine("[kiosk] No cameras detected — plug one in, then press C again.");
                    }
                    else
                    {
                        _descriptors = found;
                        var next = (_camIndex + 1) % found.Length;
                        await OpenCameraAsync(next);
                        _camIndex = next;
                        Console.WriteLine($"[kiosk] Now using camera {next}: {found[next].Name}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[kiosk] Camera reconnect failed: {ex.Message} — press C to try again.");
                }
                finally { _switching = false; }
                break;

            case Keys.R when !_busy:
                ReprintLast();
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
                var markdown = await LlmClient.GetFeedbackAsync(_cfg, pages, _session, _entra);
                pages.Clear(); // image bytes released before printing
                notice = LlmClient.NoticeFor(markdown, _session);
                if (notice is null)
                {
                    // Retain and log the text BEFORE printing, so a
                    // printer failure can never destroy the feedback.
                    var subject = _session.Subject;
                    lock (_stateLock) _lastReport = (markdown, subject);
                    if (_cfg.FeedbackLogEnabled)
                    {
                        var logged = FeedbackLog.Append(markdown, subject);
                        if (logged is not null)
                            Console.WriteLine($"[kiosk] Feedback text saved to {logged} for teacher review.");
                    }
                    Console.WriteLine("[kiosk] Feedback received. Printing…");
                    try
                    {
                        Printing.PrintMarkdown(markdown, _cfg, subject);
                    }
                    catch (Exception pex)
                    {
                        error = $"Printing failed: {pex.Message}\n[kiosk] The feedback text is safe — fix the printer, then press R (or Reports menu) to reprint without a new AI request.";
                    }
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
        _watchdog.Stop();
        if (_device is not null)
        {
            var device = _device;
            _device = null;
            try { await device.StopAsync(); device.Dispose(); } catch { }
        }
        Console.WriteLine("[kiosk] Goodbye.");
    }
}
