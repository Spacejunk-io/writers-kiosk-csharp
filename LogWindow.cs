// Writer's Kiosk (C#) — Activity Log window. GPL-3.0-or-later; see LICENSE.
//
// A modeless window (Reports → Activity Log, or the L key) showing the
// session's work history that the console window used to provide:
// token usage per report with a running total, declined submissions,
// safety notices, camera and printing events, and errors. Live-updates
// while open. Contains no student content and no identities.

namespace WritersKiosk;

public sealed class LogWindow : Form
{
    private readonly Label _summary = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        Padding = new Padding(10, 8, 10, 0),
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        AutoEllipsis = true,
    };

    private readonly TextBox _text = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = true,
        Font = new Font("Consolas", 9f),
        BackColor = Color.White,
    };

    private readonly Action<LogEntry> _onEntry;

    public LogWindow()
    {
        Text = "Activity Log — Writer's Kiosk (this session; no student content, no names)";
        ClientSize = new Size(720, 460);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowIcon = false;
        ShowInTaskbar = false;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Padding = new Padding(8, 6, 8, 6),
        };
        var close = new Button { Text = "Close", Width = 90 };
        close.Click += (_, _) => Close();
        var copy = new Button { Text = "Copy All", Width = 90 };
        copy.Click += (_, _) => CopyAll();
        buttons.Controls.AddRange([close, copy]);

        Controls.Add(_text);
        Controls.Add(buttons);
        Controls.Add(_summary);
        CancelButton = close;

        // Seed with everything logged so far, then follow live.
        _text.Text = string.Join(Environment.NewLine, KioskLog.Snapshot().Select(Format));
        _summary.Text = KioskLog.SummaryLine();
        _onEntry = entry =>
        {
            try
            {
                BeginInvoke(() =>
                {
                    _text.AppendText((_text.TextLength > 0 ? Environment.NewLine : "") + Format(entry));
                    _summary.Text = KioskLog.SummaryLine();
                });
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { } // window closing mid-event
        };
        KioskLog.EntryAdded += _onEntry;
        // Start scrolled to the newest entry.
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }

    private static string Format(LogEntry entry) =>
        $"{entry.At:h:mm:ss tt}  {(entry.IsError ? "⚠ " : "")}{entry.Message}";

    private void CopyAll()
    {
        try
        {
            if (_text.TextLength > 0)
                Clipboard.SetText(_summary.Text + Environment.NewLine + Environment.NewLine + _text.Text);
        }
        catch { /* clipboard momentarily locked by another app: non-fatal */ }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        KioskLog.EntryAdded -= _onEntry;
        base.OnFormClosed(e);
    }
}
