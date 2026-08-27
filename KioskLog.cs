// Writer's Kiosk (C#) — in-app activity log. GPL-3.0-or-later; see LICENSE.
//
// The kiosk ships as a windowed app with no console, so everything the
// old console window used to show — camera events, cooldown notes,
// token usage, declined submissions, printing status, errors — flows
// through here instead. Entries live in memory for the session (plus a
// mirror to Console for anyone running from a terminal with output
// redirected) and are viewable in the Activity Log window (Reports
// menu, or the L key). Messages are PII-free by construction: the kiosk
// never knows a student's name, and no message includes page content.

namespace WritersKiosk;

public readonly record struct LogEntry(DateTime At, string Message, bool IsError);

public static class KioskLog
{
    private const int MaxEntries = 2000;

    private static readonly object Lock = new();
    private static readonly List<LogEntry> Entries = [];

    /// <summary>Raised (on the logging thread) after each entry is
    /// stored. UI subscribers must marshal to their own thread.</summary>
    public static event Action<LogEntry>? EntryAdded;

    // ── Session counters (the "work history" at a glance) ──────────
    public static int ReportsPrinted { get; private set; }
    public static int Declined { get; private set; }
    public static int SafetyNotices { get; private set; }
    public static int Errors { get; private set; }
    public static long TokensIn { get; private set; }
    public static long TokensOut { get; private set; }
    public static long TokensTotal { get; private set; }

    public static void Info(string message) => Add(message, isError: false);

    public static void Warn(string message) => Add(message, isError: true);

    public static void CountReport() { lock (Lock) ReportsPrinted++; }

    public static void CountDeclined() { lock (Lock) Declined++; }

    public static void CountSafety() { lock (Lock) SafetyNotices++; }

    public static void CountError() { lock (Lock) Errors++; }

    /// <summary>Accumulates one report's token usage and logs it with
    /// the running session total, so spend stays visible all day.</summary>
    public static void AddTokens(long promptTokens, long completionTokens, long totalTokens)
    {
        long sessionTotal;
        lock (Lock)
        {
            TokensIn += promptTokens;
            TokensOut += completionTokens;
            TokensTotal += totalTokens;
            sessionTotal = TokensTotal;
        }
        Info($"Tokens this report: {totalTokens} ({promptTokens} in / {completionTokens} out) · session total {sessionTotal}");
    }

    public static string SummaryLine()
    {
        lock (Lock)
        {
            return $"This session — reports printed: {ReportsPrinted} · declined: {Declined} · " +
                   $"safety notices: {SafetyNotices} · errors: {Errors} · " +
                   $"tokens: {TokensTotal} ({TokensIn} in / {TokensOut} out)";
        }
    }

    public static LogEntry[] Snapshot()
    {
        lock (Lock) return [.. Entries];
    }

    private static void Add(string message, bool isError)
    {
        var entry = new LogEntry(DateTime.Now, message, isError);
        lock (Lock)
        {
            Entries.Add(entry);
            if (Entries.Count > MaxEntries)
                Entries.RemoveRange(0, MaxEntries / 4);
        }
        // Mirror to the console for terminal runs / redirected output;
        // harmless no-op in the normal windowed build.
        if (isError) Console.Error.WriteLine($"[kiosk] {message}");
        else Console.WriteLine($"[kiosk] {message}");
        EntryAdded?.Invoke(entry);
    }
}
