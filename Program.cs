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
        try
        {
            var config = KioskConfig.Load();
            Application.Run(new KioskForm(config));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Writer's Kiosk — startup error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }
    }
}
