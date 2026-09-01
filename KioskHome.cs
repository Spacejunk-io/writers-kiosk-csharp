// Writer's Kiosk (C#) — where the kiosk's files live. GPL-3.0-or-later; see LICENSE.
namespace WritersKiosk;

/// <summary>
/// Every file the kiosk reads or writes beside itself — .env,
/// assignment.txt, kiosk-ui.json, kiosk-profiles.json, feedback-log\ —
/// is addressed by a relative path, so all of them follow the process's
/// working directory. A shortcut with "Start in" left blank, a scheduled
/// task, or an autologon launcher hands the process System32 or the
/// profile root instead of the kiosk folder; the kiosk would then look
/// for its .env in the wrong place and drop its logs and profiles where
/// nobody reads them. <see cref="Resolve"/> picks the folder once, at
/// startup, and <c>Program</c> makes it the working directory.
/// </summary>
public static class KioskHome
{
    /// <summary>The file whose presence marks a kiosk folder.</summary>
    public const string Marker = ".env";

    /// <summary>
    /// The working directory wins when it already holds a .env (running
    /// from the folder, or <c>dotnet run</c> from a checkout); otherwise
    /// the folder the executable lives in. When neither has one, the
    /// executable's folder is still the answer — startup then fails with
    /// a message that names it.
    /// </summary>
    public static string Resolve(string workingDirectory, string executableDirectory, Func<string, bool> fileExists) =>
        fileExists(Path.Combine(workingDirectory, Marker)) ? workingDirectory : executableDirectory;
}
