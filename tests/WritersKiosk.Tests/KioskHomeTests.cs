// Writer's Kiosk tests — the kiosk folder is chosen, not inherited. GPL-3.0-or-later.
using Xunit;

namespace WritersKiosk.Tests;

public sealed class KioskHomeTests
{
    private static Func<string, bool> Exists(params string[] present) =>
        path => present.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void TheWorkingDirectoryWinsWhenItHoldsAnEnv()
    {
        // `dotnet run` from a checkout, or running from the kiosk folder.
        var home = KioskHome.Resolve(@"C:\repo", @"C:\repo\bin\Release\net10.0", Exists(@"C:\repo\.env"));
        Assert.Equal(@"C:\repo", home);
    }

    [Fact]
    public void TheExecutableFolderWinsWhenTheLauncherStartedElsewhere()
    {
        // A shortcut with "Start in" blank hands the process System32.
        var home = KioskHome.Resolve(@"C:\Windows\System32", @"D:\kiosk", Exists(@"D:\kiosk\.env"));
        Assert.Equal(@"D:\kiosk", home);
    }

    [Fact]
    public void TheExecutableFolderIsStillTheAnswerWhenNeitherHasAnEnv()
    {
        // Startup will fail loudly, naming this folder.
        var home = KioskHome.Resolve(@"C:\Windows\System32", @"D:\kiosk", Exists());
        Assert.Equal(@"D:\kiosk", home);
    }

    [Fact]
    public void ProgramAnchorsTheWorkingDirectoryBeforeLoadingConfig()
    {
        // The unit above is pure; this pins the wiring that makes it matter.
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "Program.cs"));
        var anchor = program.IndexOf("Directory.SetCurrentDirectory(home)", StringComparison.Ordinal);
        var load = program.IndexOf("KioskConfig.Load()", StringComparison.Ordinal);
        Assert.Contains("KioskHome.Resolve(", program);
        Assert.True(anchor >= 0, "Program.cs no longer anchors the working directory.");
        Assert.True(load > anchor, "The working directory must be anchored before .env is read.");
    }

    /// <summary>Walks up from the test binaries to the checkout root.</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WritersKiosk.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("WritersKiosk.csproj not found above the test binaries.");
    }
}
