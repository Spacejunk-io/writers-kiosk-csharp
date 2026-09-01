// Writer's Kiosk (C#) — entry point. GPL-3.0-or-later; see LICENSE.
namespace WritersKiosk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Windowed app — there is no console, so a startup failure
        // (bad .env, missing key) must surface as a dialog.
        ApplicationConfiguration.Initialize();

        // Anchor every relative path (.env, logs, profiles) to the kiosk
        // folder, whatever working directory the launcher handed us.
        var home = KioskHome.Resolve(
            Directory.GetCurrentDirectory(), AppContext.BaseDirectory, File.Exists);
        try
        {
            Directory.SetCurrentDirectory(home);
            var config = KioskConfig.Load();
            Application.Run(new KioskForm(config));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{ex.Message}\n\nKiosk folder: {home}", "Writer's Kiosk — startup error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }
}
