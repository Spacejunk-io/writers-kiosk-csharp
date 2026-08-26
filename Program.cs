// Writer's Kiosk (C#) — entry point. GPL-3.0-or-later; see LICENSE.
namespace WritersKiosk;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            var config = KioskConfig.Load();
            ApplicationConfiguration.Initialize();
            Application.Run(new KioskForm(config));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\n[Writer's Kiosk] Startup error: {ex.Message}");
            Console.Error.WriteLine("Press Enter to close.");
            Console.ReadLine();
            Environment.Exit(1);
        }
    }
}
