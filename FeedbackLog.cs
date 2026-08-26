// Writer's Kiosk (C#) — teacher review log. GPL-3.0-or-later; see LICENSE.
//
// Backs the printed footer's promise that feedback is "reviewed under
// your teacher's direction": every report's TEXT is appended to a daily
// Markdown file the teacher can read after class. Privacy-consistent by
// construction — the kiosk never saves student images, and reports are
// generated under rules that exclude student names and identifiers.

namespace WritersKiosk;

public static class FeedbackLog
{
    public const string Folder = "feedback-log";

    /// <summary>
    /// Appends one report to today's log file. Returns the path written,
    /// or null if logging failed (never fatal — printing proceeds).
    /// </summary>
    public static string? Append(string markdown, string subject)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = Path.Combine(Folder, DateTime.Now.ToString("yyyy-MM-dd") + ".md");
            var entry = $"\n\n## {DateTime.Now:h:mm tt} — {subject}\n\n{markdown}\n\n---\n";
            if (!File.Exists(path))
                entry = $"# Writer's Kiosk feedback log — {DateTime.Now:MMMM d, yyyy}\n\n" +
                        "Feedback text only. The kiosk never saves student images, and reports " +
                        "are generated under rules that exclude student names and identifiers." +
                        entry;
            File.AppendAllText(path, entry);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
