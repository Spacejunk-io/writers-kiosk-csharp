// Writer's Kiosk tests — teacher-profile store. GPL-3.0-or-later.
using Xunit;

namespace WritersKiosk.Tests;

public sealed class ProfilesTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(), $"kiosk-profiles-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_file); } catch { }
    }

    [Fact]
    public void ProfilesRoundTripThroughTheStoreFile()
    {
        var store = new ProfileStore
        {
            Active = "Period 3 — ELL",
            Profiles =
            [
                new KioskProfile
                {
                    Name = "Period 3 — ELL",
                    Level = Subjects.Middle,
                    Subject = "Social Studies",
                    Grade = 8,
                    Band = "approaching",
                    Bilingual = "Spanish",
                    BilingualTwoColumn = true,
                    FlipVertical = true,
                    Enhance = false,
                    CameraIndex = 1,
                },
                new KioskProfile { Name = "AP English", Level = Subjects.High, Subject = "English", Grade = 12, Band = "advanced" },
            ],
        };
        Assert.True(store.Save(_file));

        var loaded = ProfileStore.Load(_file);
        Assert.Equal(2, loaded.Profiles.Count);
        Assert.Equal("Period 3 — ELL", loaded.Active);
        var ell = loaded.ActiveProfile!;
        Assert.Equal("Spanish", ell.Bilingual);
        Assert.True(ell.BilingualTwoColumn);
        Assert.True(ell.FlipVertical);
        Assert.False(ell.Enhance);
        Assert.Equal(1, ell.CameraIndex);
        Assert.Equal("approaching", ell.Band);
    }

    [Fact]
    public void MissingFileYieldsAnEmptyStore()
    {
        var loaded = ProfileStore.Load(_file);
        Assert.Empty(loaded.Profiles);
        Assert.Null(loaded.Active);
    }

    [Fact]
    public void CorruptFileYieldsAnEmptyStore()
    {
        File.WriteAllText(_file, "{{{ nope");
        var loaded = ProfileStore.Load(_file);
        Assert.Empty(loaded.Profiles);
    }

    [Fact]
    public void SanitizeForcesEveryFieldValid()
    {
        var profile = new KioskProfile
        {
            Name = "  spaced  ",
            Level = "elementary",   // unknown level
            Subject = "Alchemy",    // not in any catalog
            Grade = 3,              // out of range
            Band = "super",         // unknown band
            Bilingual = "   ",      // whitespace = off
            CameraIndex = -2,
        };
        profile.Sanitize();
        Assert.Equal("spaced", profile.Name);
        Assert.Equal(Subjects.Middle, profile.Level);
        Assert.Equal("Social Studies", profile.Subject);
        Assert.Equal(6, profile.Grade);
        Assert.Equal("on", profile.Band);
        Assert.Null(profile.Bilingual);
        Assert.Equal(0, profile.CameraIndex);
    }

    [Fact]
    public void SanitizeClampsGradeIntoTheLevel()
    {
        var high = new KioskProfile { Level = Subjects.High, Subject = "English", Grade = 7 };
        high.Sanitize();
        Assert.Equal(9, high.Grade);
        var middle = new KioskProfile { Level = Subjects.Middle, Grade = 12 };
        middle.Sanitize();
        Assert.Equal(8, middle.Grade);
    }

    [Fact]
    public void ActivePointingAtAMissingProfileIsCleared()
    {
        new ProfileStore
        {
            Active = "Deleted Elsewhere",
            Profiles = [new KioskProfile { Name = "Kept" }],
        }.Save(_file);
        var loaded = ProfileStore.Load(_file);
        Assert.Null(loaded.Active);
        Assert.Single(loaded.Profiles);
    }

    [Fact]
    public void DuplicateAndNamelessProfilesAreDropped()
    {
        new ProfileStore
        {
            Profiles =
            [
                new KioskProfile { Name = "Twin", Grade = 6 },
                new KioskProfile { Name = "Twin", Grade = 8 },
                new KioskProfile { Name = "" },
            ],
        }.Save(_file);
        var loaded = ProfileStore.Load(_file);
        var twin = Assert.Single(loaded.Profiles);
        Assert.Equal("Twin", twin.Name);
        Assert.Equal(6, twin.Grade); // first one wins
    }
}
