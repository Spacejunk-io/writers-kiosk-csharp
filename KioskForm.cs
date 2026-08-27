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
    private readonly ProfileStore _profiles;
    private readonly int _startupCameraIndex;
    private ToolStripMenuItem _middleMenu = null!;
    private ToolStripMenuItem _highMenu = null!;
    private ToolStripMenuItem _gradeBandMenu = null!;
    private ToolStripMenuItem _bilingualMenu = null!;
    private ToolStripMenuItem _twoColumnItem = null!;
    private ToolStripMenuItem _profilesMenu = null!;
    private Font _menuFontRegular = null!;
    private Font _menuFontBold = null!;
    private LogWindow? _logWindow;

    private static readonly string[] BilingualLanguages =
    [
        "Spanish", "French", "Arabic", "Chinese (Simplified)",
        "Haitian Creole", "Korean", "Portuguese", "Russian",
        "Ukrainian", "Vietnamese",
    ];
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

        // The active profile's presets win at every launch, so the
        // station always opens in the state the teacher chose. With no
        // active profile, the last session's settings carry over as
        // before. The daily assignment always comes from assignment.txt.
        _profiles = ProfileStore.Load();
        var startup = _profiles.ActiveProfile;
        if (startup is not null)
        {
            _session.ApplyProfile(startup);
            _session.SaveUiState();
        }

        if (cfg.Provider == Provider.Azure && cfg.AzureUseEntra)
            _entra = new EntraTokenProvider(cfg.AzureTenantId, cfg.AzureClientId);
        _enhancer = new Enhancer(startup?.Enhance ?? cfg.Enhance);
        _flipV = startup?.FlipVertical ?? cfg.FlipVertical;
        _flipH = startup?.FlipHorizontal ?? cfg.FlipHorizontal;
        _startupCameraIndex = startup?.CameraIndex ?? cfg.CameraIndex;

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
            KioskLog.Info("Loaded teacher assignment context from assignment.txt.");
        if (startup is not null)
            KioskLog.Info($"Startup profile \"{startup.Name}\" applied.");
        KioskLog.Info($"Feedback tuned for: grade {_session.Grade} {_session.Subject}, {_session.BandDisplay}" +
            (_session.BilingualLanguage is { } bl ? $", bilingual English + {bl}" : "") +
            " (change via the menu bar).");
    }

    // ── Menu bar: Assignment · Middle School · High School ·
    //    Grade & Band · Bilingual · Profiles · Reports · Help ─────────

    private void BuildMenu()
    {
        var menu = new MenuStrip();
        _menuFontRegular = menu.Font;
        _menuFontBold = new Font(_menuFontRegular, FontStyle.Bold);

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
            "Activity Log…  (L)", null, (_, _) => ShowActivityLog()));
        reports.DropDownItems.Add(new ToolStripMenuItem(
            "Open Feedback Log Folder", null, (_, _) => OpenLogFolder()));

        _gradeBandMenu = new ToolStripMenuItem("Grade && Band");
        for (var g = 6; g <= 12; g++)
        {
            var grade = g;
            _gradeBandMenu.DropDownItems.Add(new ToolStripMenuItem(
                $"Grade {grade}", null, (_, _) => SelectGrade(grade)));
        }
        _gradeBandMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (key, display) in Bands.All)
        {
            var bandKey = key;
            _gradeBandMenu.DropDownItems.Add(new ToolStripMenuItem(
                display, null, (_, _) => SelectBand(bandKey)));
        }

        _bilingualMenu = new ToolStripMenuItem("Bilingual");
        _bilingualMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Off — English only", null, (_, _) => SelectBilingual(null)));
        _bilingualMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var language in BilingualLanguages)
        {
            var lang = language;
            _bilingualMenu.DropDownItems.Add(new ToolStripMenuItem(
                lang, null, (_, _) => SelectBilingual(lang)));
        }
        _bilingualMenu.DropDownItems.Add(new ToolStripSeparator());
        _bilingualMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Other Language…", null, (_, _) => ChooseCustomLanguage()));
        _bilingualMenu.DropDownItems.Add(new ToolStripSeparator());
        _twoColumnItem = new ToolStripMenuItem(
            "Two-Column Layout (languages side by side)", null, (_, _) => ToggleTwoColumn());
        _bilingualMenu.DropDownItems.Add(_twoColumnItem);

        _profilesMenu = new ToolStripMenuItem("Profiles");
        RebuildProfilesMenu();

        var help = new ToolStripMenuItem("Help && Support");
        help.DropDownItems.Add(new ToolStripMenuItem("Help", null, (_, _) => ShowHelp()));
        help.DropDownItems.Add(new ToolStripMenuItem("Hotkeys", null, (_, _) => ShowHotkeys()));
        help.DropDownItems.Add(new ToolStripMenuItem("About", null, (_, _) => ShowAbout()));

        menu.Items.AddRange([assignment, _middleMenu, _highMenu, _gradeBandMenu, _bilingualMenu, _profilesMenu, reports, help]);
        MainMenuStrip = menu;
        Controls.Add(menu);
        _view.BringToFront();
        RefreshSubjectChecks();
    }

    /// <summary>Applies a level+subject choice immediately — no restart.
    /// The grade snaps into the chosen level's range if needed.</summary>
    private void SelectSubject(string level, string subject)
    {
        _session.Level = level;
        _session.Subject = subject;
        _session.Grade = level == Subjects.High
            ? Math.Clamp(_session.Grade, 9, 12)
            : Math.Clamp(_session.Grade, 6, 8);
        _session.SaveUiState();
        RefreshSubjectChecks();
        KioskLog.Info($"Feedback now tuned for: grade {_session.Grade} {subject}, {_session.BandDisplay}.");
    }

    /// <summary>Grade choice; the school level follows the grade, and the
    /// subject falls back if the new level's catalog lacks it.</summary>
    private void SelectGrade(int grade)
    {
        _session.Grade = grade;
        var level = grade >= 9 ? Subjects.High : Subjects.Middle;
        if (level != _session.Level)
        {
            _session.Level = level;
            var catalog = level == Subjects.High ? Subjects.HighSubjects : Subjects.MiddleSubjects;
            if (!catalog.Contains(_session.Subject))
                _session.Subject = "Social Studies"; // present in both catalogs
        }
        _session.SaveUiState();
        RefreshSubjectChecks();
        KioskLog.Info($"Feedback now tuned for: grade {grade} {_session.Subject}, {_session.BandDisplay}.");
    }

    private void SelectBand(string bandKey)
    {
        _session.Band = bandKey;
        _session.SaveUiState();
        RefreshSubjectChecks();
        KioskLog.Info($"Response band: {_session.BandDisplay}.");
    }

    private void SelectBilingual(string? language)
    {
        _session.BilingualLanguage = language;
        _session.SaveUiState();
        RefreshSubjectChecks();
        KioskLog.Info(language is null
            ? "Bilingual feedback off — English only."
            : $"Bilingual feedback on: English + {language} ({(_session.BilingualTwoColumn ? "two-column" : "stacked")} layout).");
    }

    private void ToggleTwoColumn()
    {
        _session.BilingualTwoColumn = !_session.BilingualTwoColumn;
        _session.SaveUiState();
        RefreshSubjectChecks();
        KioskLog.Info(_session.BilingualTwoColumn
            ? "Bilingual layout: two columns, languages side by side — each section starts level in both languages."
            : "Bilingual layout: stacked — each item followed directly by its translation.");
    }

    private void ChooseCustomLanguage()
    {
        if (PromptForText("Bilingual feedback — other language",
                "Language name (in English), e.g. \"Amharic\":", "") is { } lang)
            SelectBilingual(lang);
    }

    /// <summary>One-line text prompt dialog. Returns the trimmed text,
    /// or null on cancel/empty.</summary>
    private string? PromptForText(string title, string label, string initial)
    {
        using var dialog = new Form
        {
            Text = title,
            ClientSize = new Size(420, 110),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowIcon = false,
        };
        var caption = new Label { Text = label, Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 6, 8, 0) };
        var box = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 10f), Margin = new Padding(8), Text = initial };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        buttons.Controls.AddRange([ok, cancel]);
        dialog.Controls.Add(box);
        dialog.Controls.Add(caption);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        box.SelectAll();
        return dialog.ShowDialog(this) == DialogResult.OK && box.Text.Trim() is { Length: > 0 } text
            ? text
            : null;
    }

    // ── Profiles: named snapshots of every teacher-tunable setting ────

    /// <summary>Snapshots the current teacher-tunable state under a
    /// name. The daily assignment stays out on purpose (see Profiles.cs).</summary>
    private KioskProfile CaptureProfile(string name)
    {
        bool flipV, flipH;
        lock (_stateLock) { flipV = _flipV; flipH = _flipH; }
        return new KioskProfile
        {
            Name = name,
            Level = _session.Level,
            Subject = _session.Subject,
            Grade = _session.Grade,
            Band = _session.Band,
            Bilingual = _session.BilingualLanguage,
            BilingualTwoColumn = _session.BilingualTwoColumn,
            FlipVertical = flipV,
            FlipHorizontal = flipH,
            Enhance = _enhancer.Enabled,
            CameraIndex = _camIndex,
        };
    }

    private void RebuildProfilesMenu()
    {
        _profilesMenu.DropDownItems.Clear();
        foreach (var p in _profiles.Profiles)
        {
            var profile = p;
            _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(
                profile.Name, null, async (_, _) => await SelectProfileAsync(profile))
            { Checked = _profiles.Active == profile.Name });
        }
        if (_profiles.Profiles.Count > 0)
            _profilesMenu.DropDownItems.Add(new ToolStripSeparator());
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(
            "No Startup Profile — reopen with last session's settings", null,
            (_, _) => ClearActiveProfile())
        { Checked = _profiles.Active is null });
        _profilesMenu.DropDownItems.Add(new ToolStripSeparator());
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(
            "Save Current Settings as New Profile…", null, (_, _) => SaveNewProfile()));
        var active = _profiles.ActiveProfile;
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(
            active is null ? "Update Active Profile with Current Settings" : $"Update \"{active.Name}\" with Current Settings",
            null, (_, _) => UpdateActiveProfile())
        { Enabled = active is not null });
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(
            active is null ? "Rename Active Profile…" : $"Rename \"{active.Name}\"…",
            null, (_, _) => RenameActiveProfile())
        { Enabled = active is not null });
        _profilesMenu.DropDownItems.Add(new ToolStripMenuItem(
            active is null ? "Delete Active Profile…" : $"Delete \"{active.Name}\"…",
            null, (_, _) => DeleteActiveProfile())
        { Enabled = active is not null });
        _profilesMenu.Font = _profiles.Active is null ? _menuFontRegular : _menuFontBold;
    }

    /// <summary>Applies a profile immediately and marks it as the
    /// startup profile for future launches.</summary>
    private async Task SelectProfileAsync(KioskProfile profile)
    {
        _session.ApplyProfile(profile);
        _session.SaveUiState();
        lock (_stateLock)
        {
            _flipV = profile.FlipVertical;
            _flipH = profile.FlipHorizontal;
        }
        _enhancer.Enabled = profile.Enhance;
        _profiles.Active = profile.Name;
        _profiles.Save();
        RefreshSubjectChecks();
        RebuildProfilesMenu();
        KioskLog.Info($"Profile \"{profile.Name}\" applied: grade {_session.Grade} {_session.Subject}, {_session.BandDisplay}" +
            (_session.BilingualLanguage is { } bl ? $", bilingual English + {bl}" : "") +
            ". It will also load at every launch.");

        // Follow the profile's camera when possible; never mid-report.
        if (profile.CameraIndex != _camIndex && !_busy && !_switching
            && profile.CameraIndex < _descriptors.Count)
        {
            _switching = true;
            try
            {
                await OpenCameraAsync(profile.CameraIndex);
                _camIndex = profile.CameraIndex;
                KioskLog.Info($"Now using camera {_camIndex}: {_descriptors[_camIndex].Name}");
            }
            catch (Exception ex)
            {
                KioskLog.Warn($"Profile camera {profile.CameraIndex} could not be opened: {ex.Message} — keeping the current camera.");
            }
            finally { _switching = false; }
        }
    }

    private void ClearActiveProfile()
    {
        _profiles.Active = null;
        _profiles.Save();
        RebuildProfilesMenu();
        KioskLog.Info("No startup profile — the kiosk will reopen with whatever settings were in use last.");
    }

    private void SaveNewProfile()
    {
        var name = PromptForText("Save profile", "Profile name, e.g. \"Period 3 — ELL Social Studies\":", "");
        if (name is null) return;
        var existing = _profiles.Profiles.FindIndex(p => p.Name == name);
        if (existing >= 0 &&
            MessageBox.Show(this, $"A profile named \"{name}\" already exists. Replace it?",
                "Save profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var profile = CaptureProfile(name);
        if (existing >= 0) _profiles.Profiles[existing] = profile;
        else _profiles.Profiles.Add(profile);
        _profiles.Active = name;
        if (!_profiles.Save())
            KioskLog.Warn($"Could not write {ProfileStore.FileName} — the profile exists for this session only.");
        RebuildProfilesMenu();
        KioskLog.Info($"Profile \"{name}\" saved with the current settings; it will load at every launch.");
    }

    private void UpdateActiveProfile()
    {
        if (_profiles.ActiveProfile is not { } active) return;
        var index = _profiles.Profiles.IndexOf(active);
        _profiles.Profiles[index] = CaptureProfile(active.Name);
        _profiles.Save();
        RebuildProfilesMenu();
        KioskLog.Info($"Profile \"{active.Name}\" updated with the current settings.");
    }

    private void RenameActiveProfile()
    {
        if (_profiles.ActiveProfile is not { } active) return;
        var name = PromptForText("Rename profile", "New name:", active.Name);
        if (name is null || name == active.Name) return;
        if (_profiles.Profiles.Any(p => p.Name == name))
        {
            MessageBox.Show(this, $"A profile named \"{name}\" already exists.",
                "Rename profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var oldName = active.Name;
        active.Name = name;
        _profiles.Active = name;
        _profiles.Save();
        RebuildProfilesMenu();
        KioskLog.Info($"Profile \"{oldName}\" renamed to \"{name}\".");
    }

    private void DeleteActiveProfile()
    {
        if (_profiles.ActiveProfile is not { } active) return;
        if (MessageBox.Show(this,
                $"Delete profile \"{active.Name}\"? Current settings stay as they are; the kiosk will simply stop loading this profile at startup.",
                "Delete profile", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _profiles.Profiles.Remove(active);
        _profiles.Active = null;
        _profiles.Save();
        RebuildProfilesMenu();
        KioskLog.Info($"Profile \"{active.Name}\" deleted.");
    }

    private void ShowActivityLog()
    {
        if (_logWindow is { IsDisposed: false })
        {
            _logWindow.Activate();
            return;
        }
        _logWindow = new LogWindow();
        _logWindow.FormClosed += (_, _) => _logWindow = null;
        _logWindow.Show(this);
    }

    private void RefreshSubjectChecks()
    {
        foreach (var (menu, level) in new[] { (_middleMenu, Subjects.Middle), (_highMenu, Subjects.High) })
        {
            var isActiveLevel = _session.Level == level;
            menu.Font = isActiveLevel ? _menuFontBold : _menuFontRegular;
            foreach (ToolStripMenuItem item in menu.DropDownItems)
                item.Checked = isActiveLevel && item.Text == _session.Subject;
        }
        foreach (var entry in _gradeBandMenu.DropDownItems)
        {
            if (entry is not ToolStripMenuItem item) continue;
            item.Checked = item.Text == $"Grade {_session.Grade}" || item.Text == _session.BandDisplay;
        }
        var current = _session.BilingualLanguage;
        foreach (var entry in _bilingualMenu.DropDownItems)
        {
            if (entry is not ToolStripMenuItem item || item == _twoColumnItem) continue;
            item.Checked = current is null
                ? item.Text == "Off — English only"
                : item.Text == current;
        }
        // A custom language shows as a checked mark on "Other Language…".
        if (current is not null && !BilingualLanguages.Contains(current))
            foreach (var entry in _bilingualMenu.DropDownItems)
                if (entry is ToolStripMenuItem { Text: "Other Language…" } other)
                    other.Checked = true;
        _twoColumnItem.Checked = _session.BilingualTwoColumn;
        _bilingualMenu.Font = current is null ? _menuFontRegular : _menuFontBold;
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
            KioskLog.Info(text.Length > 0
                ? $"Assignment context updated (saved to {_cfg.AssignmentFile}); applies to the next capture."
                : "Assignment context cleared — feedback returns to general mode.");
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"Assignment applied for this session, but saving failed: {ex.Message}");
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
            KioskLog.Info("No report to reprint yet this session.");
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
                    if (error is null) KioskLog.Info("Reprint sent to the printer.");
                    else KioskLog.Warn($"Reprint failed: {error}");
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
            KioskLog.Warn($"Could not open the log folder: {ex.Message}");
        }
    }

    private void ShowHelp() => MessageBox.Show(this,
        """
        Writer's Kiosk gives students printed AI feedback on their written classwork.

        1. Place the page under the document camera; check it is sharp and fills the preview.
        2. Press SPACE — the preview dims while the report is generated (10–25 seconds) and printing starts by itself.
        3. Multi-page work: press N on each earlier page, then SPACE on the last one.

        The menu bar tunes the feedback and every choice takes effect immediately: school level & subject, the specific grade (6-12) and response band (from "Emerging — building foundations" to "Advanced"), a Bilingual option that pairs every feedback item with a chosen home language (for multilingual learners in any subject; choose two-column to print the languages side by side, or stacked), and "Assignment" to describe today's task.

        The Profiles menu saves all of those settings (plus flips, enhancement, and the camera choice) under a name — e.g. one profile per class period. The checked profile loads automatically every time the kiosk opens; today's assignment always comes from assignment.txt, never from a profile.

        Every report's text (never images, never names) is saved to the feedback-log folder for teacher review — see the Reports menu, which can also reprint the last report after a printer jam (or press R) and open the Activity Log (or press L): the session's history of reports, token usage, declined pages, and errors. If the camera cable is bumped loose, the title bar says so; reconnect it and press C.

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
        L      — open the Activity Log (session history: tokens, declines, errors)
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
                KioskLog.Info("Keyless Azure mode: verifying district sign-in…");
                await _entra.GetTokenAsync();
                KioskLog.Info("District sign-in OK — no API key in use.");
            }

            _descriptors = new CaptureDevices().EnumerateDescriptors().ToArray();
            if (_descriptors.Count == 0)
                throw new InvalidOperationException("No cameras detected. Is the document camera plugged in?");

            KioskLog.Info("Cameras detected (press C in the kiosk window to switch):");
            for (var i = 0; i < _descriptors.Count; i++)
                KioskLog.Info($"  {i}: {_descriptors[i].Name}");

            _camIndex = Math.Clamp(_startupCameraIndex, 0, _descriptors.Count - 1);
            KioskLog.Info($"Opening camera {_camIndex}…");
            await OpenCameraAsync(_camIndex);
            _watchdog.Tick += OnWatchdogTick;
            _watchdog.Start();
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"Startup error: {ex.Message}");
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
        KioskLog.Info($"Camera streaming at {characteristics.Width}x{characteristics.Height}.");
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
            KioskLog.Warn("Camera signal lost — check the USB cable, then press C to reconnect.");
        }
        else if (!stale && _cameraLost)
        {
            _cameraLost = false;
            if (!_busy) Text = TitleReady;
            KioskLog.Info("Camera signal restored.");
        }
    }

    private int _frameBusy;

    /// <summary>Runs on FlashCap's capture thread for every frame.</summary>
    private void OnFrame(PixelBufferScope scope)
    {
        // Decode + enhance at full document-camera resolution can take
        // longer than one frame interval; letting frames queue behind
        // it only builds preview lag. Drop the frame instead — the next
        // one is newer anyway.
        if (Interlocked.CompareExchange(ref _frameBusy, 1, 0) != 0)
        {
            scope.ReleaseNow();
            return;
        }
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
                KioskLog.Warn($"Camera frame error: {ex.Message}");
            }
        }
        finally
        {
            Volatile.Write(ref _frameBusy, 0);
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
                KioskLog.Info($"Vertical flip {(_flipV ? "on" : "off")}.");
                break;

            case Keys.H:
                lock (_stateLock) _flipH = !_flipH;
                KioskLog.Info($"Horizontal flip {(_flipH ? "on" : "off")}.");
                break;

            case Keys.E:
                _enhancer.Enabled = !_enhancer.Enabled;
                KioskLog.Info($"Auto image enhancement {(_enhancer.Enabled ? "on" : "off")}.");
                break;

            case Keys.L:
                ShowActivityLog();
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
                        KioskLog.Warn("No cameras detected — plug one in, then press C again.");
                    }
                    else
                    {
                        _descriptors = found;
                        var next = (_camIndex + 1) % found.Length;
                        await OpenCameraAsync(next);
                        _camIndex = next;
                        KioskLog.Info($"Now using camera {next}: {found[next].Name}");
                    }
                }
                catch (Exception ex)
                {
                    KioskLog.Warn($"Camera reconnect failed: {ex.Message} — press C to try again.");
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
            KioskLog.Info($"One moment — page just captured. Try again in {SecondsLeft(_captureOkAt)}s.");
            return;
        }
        if (_batch.Count >= MaxPages - 1)
        {
            KioskLog.Info($"Page limit ({MaxPages}) nearly reached — press SPACE to capture the last page and get feedback.");
            return;
        }
        using var frame = SnapshotFrame();
        if (frame is null) return;
        _batch.Add(ImageOps.EncodeJpeg(frame));
        _captureOkAt = DateTime.Now.AddSeconds(1.5);
        KioskLog.Info($"Page {_batch.Count} captured. Place the next page, then N again or SPACE to finish.");
        Text = $"Writer's Kiosk — {_batch.Count} page(s) captured · SPACE: capture last page & get feedback";
    }

    private void HandleSubmit()
    {
        if (DateTime.Now < _submitOkAt)
        {
            KioskLog.Info($"Cooldown — next submission in {SecondsLeft(_submitOkAt)}s (prevents accidental repeat API calls).");
            return;
        }
        if (DateTime.Now < _captureOkAt)
        {
            KioskLog.Info($"One moment — page just captured. Try again in {SecondsLeft(_captureOkAt)}s.");
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
        KioskLog.Info($"Submitting {pages.Count} page(s) ({kb} KB). Requesting feedback…");

        lock (_stateLock) _busy = true;
        Text = TitleBusy;

        Task.Run(async () =>
        {
            string[]? notice = null;
            string? error = null;
            var safety = false;
            try
            {
                var markdown = await LlmClient.GetFeedbackAsync(_cfg, pages, _session, _entra);
                pages.Clear(); // image bytes released before printing
                if (LlmClient.IsSafetyFlag(markdown))
                {
                    // Possible real disclosure: nothing prints, nothing is
                    // logged as feedback; the student sees the same calm
                    // notice style as any refusal, and staff are alerted
                    // through SafetyAlert (activity log, safety log, and
                    // the district flow when configured).
                    safety = true;
                    notice =
                    [
                        "Nothing was printed.",
                        "Please bring this page to your teacher.",
                    ];
                    await SafetyAlert.RaiseAsync(_cfg, _session.Subject);
                    markdown = "";
                }
                else
                {
                    notice = LlmClient.NoticeFor(markdown, _session);
                }
                if (markdown.Length > 0 && notice is null)
                {
                    // Retain and log the text BEFORE printing, so a
                    // printer failure can never destroy the feedback.
                    var subject = _session.Subject;
                    lock (_stateLock) _lastReport = (markdown, subject);
                    if (_cfg.FeedbackLogEnabled)
                    {
                        var logged = FeedbackLog.Append(markdown, subject);
                        if (logged is not null)
                            KioskLog.Info($"Feedback text saved to {logged} for teacher review.");
                    }
                    KioskLog.Info("Feedback received. Printing…");
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
                    {
                        KioskLog.CountError();
                        KioskLog.Warn($"This capture failed: {error} — ready to try again.");
                    }
                    else if (notice is not null)
                    {
                        // A safety notice is counted by SafetyAlert, not
                        // as an ordinary declined submission.
                        if (!safety) KioskLog.CountDeclined();
                        KioskLog.Info($"Not printed: {string.Join(" ", notice)}");
                    }
                    else
                    {
                        KioskLog.CountReport();
                        KioskLog.Info("Report sent to the printer. Ready for the next student.");
                    }
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
        KioskLog.Info("Goodbye.");
    }
}
