// Writer's Kiosk tests — session-state persistence. GPL-3.0-or-later.
using Xunit;

namespace WritersKiosk.Tests;

public sealed class SessionSettingsTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(), $"kiosk-ui-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_file); } catch { }
    }

    [Fact]
    public void SettingsRoundTripThroughTheUiFile()
    {
        var original = new SessionSettings
        {
            Level = Subjects.High,
            Subject = "English",
            Grade = 11,
            Band = "advanced",
            BilingualLanguage = "Arabic",
            BilingualTwoColumn = false,
        };
        original.SaveUiState(_file);

        var loaded = SessionSettings.Load(null, _file);
        Assert.Equal(Subjects.High, loaded.Level);
        Assert.Equal("English", loaded.Subject);
        Assert.Equal(11, loaded.Grade);
        Assert.Equal("advanced", loaded.Band);
        Assert.Equal("Arabic", loaded.BilingualLanguage);
        Assert.False(loaded.BilingualTwoColumn);
    }

    [Fact]
    public void GradeIsClampedToTheLoadedLevel()
    {
        File.WriteAllText(_file,
            """{"level":"high","subject":"English","grade":6,"band":"on"}""");
        var loaded = SessionSettings.Load(null, _file);
        Assert.Equal(9, loaded.Grade);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaults()
    {
        File.WriteAllText(_file, "not json at all {{{");
        var loaded = SessionSettings.Load(null, _file);
        Assert.Equal(Subjects.Middle, loaded.Level);
        Assert.Equal("Social Studies", loaded.Subject);
        Assert.Equal(8, loaded.Grade);
        Assert.Equal("on", loaded.Band);
        Assert.Null(loaded.BilingualLanguage);
    }

    [Fact]
    public void UnknownSubjectIsIgnored()
    {
        File.WriteAllText(_file,
            """{"level":"middle","subject":"Alchemy","grade":7,"band":"on"}""");
        var loaded = SessionSettings.Load(null, _file);
        Assert.Equal("Social Studies", loaded.Subject);
    }

    [Fact]
    public void OlderUiFileWithoutColumnsKeepsTwoColumnDefault()
    {
        File.WriteAllText(_file,
            """{"level":"middle","subject":"Social Studies","grade":8,"band":"on","bilingual":"Spanish"}""");
        var loaded = SessionSettings.Load(null, _file);
        Assert.True(loaded.BilingualTwoColumn);
        Assert.Equal("Spanish", loaded.BilingualLanguage);
    }

    [Fact]
    public void ApplyProfileSetsEverySessionPresetButNotTheAssignment()
    {
        var session = new SessionSettings { AssignmentContext = "today's task" };
        session.ApplyProfile(new KioskProfile
        {
            Name = "AP Bio",
            Level = Subjects.High,
            Subject = "Science",
            Grade = 12,
            Band = "exceeding",
            Bilingual = "Korean",
            BilingualTwoColumn = false,
        });
        Assert.Equal(Subjects.High, session.Level);
        Assert.Equal("Science", session.Subject);
        Assert.Equal(12, session.Grade);
        Assert.Equal("exceeding", session.Band);
        Assert.Equal("Korean", session.BilingualLanguage);
        Assert.False(session.BilingualTwoColumn);
        Assert.Equal("today's task", session.AssignmentContext);
    }
}
