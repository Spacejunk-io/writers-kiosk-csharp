// Writer's Kiosk (C#) — teacher profiles. GPL-3.0-or-later; see LICENSE.
//
// A profile is a named snapshot of every teacher-tunable element of the
// kiosk: school level, subject, grade, response band, bilingual language
// and layout, image flips, enhancement, and which camera to use. The
// active profile's presets are applied at every launch, so a station
// always opens in a known state (e.g. "Period 3 — ELL Social Studies").
// Teachers can keep any number of profiles and switch between them from
// the Profiles menu mid-session.
//
// Deliberately NOT in a profile: today's assignment context (it lives in
// assignment.txt and changes daily — a stale profile must never
// overwrite the day's task) and machine/deployment settings (API
// credentials, printer, station name), which stay in .env.
//
// Stored in kiosk-profiles.json next to the executable — plain JSON, no
// secrets, safe to copy between stations.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WritersKiosk;

public sealed class KioskProfile
{
    public string Name { get; set; } = "";
    public string Level { get; set; } = Subjects.Middle;
    public string Subject { get; set; } = "Social Studies";
    public int Grade { get; set; } = 8;
    public string Band { get; set; } = "on";
    /// <summary>Home/partner language for bilingual feedback, or null
    /// for English-only.</summary>
    public string? Bilingual { get; set; }
    /// <summary>Print bilingual reports side by side in two columns
    /// (true) or stacked line by line (false).</summary>
    public bool BilingualTwoColumn { get; set; } = true;
    public bool FlipVertical { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool Enhance { get; set; } = true;
    public int CameraIndex { get; set; }

    /// <summary>Forces every field into a valid state, mirroring the
    /// session-settings rules — a hand-edited or stale file must never
    /// put the kiosk into an impossible configuration.</summary>
    public void Sanitize()
    {
        Name = Name.Trim();
        if (Level is not (Subjects.Middle or Subjects.High))
            Level = Grade >= 9 ? Subjects.High : Subjects.Middle;
        Grade = Level == Subjects.High ? Math.Clamp(Grade, 9, 12) : Math.Clamp(Grade, 6, 8);
        var catalog = Level == Subjects.High ? Subjects.HighSubjects : Subjects.MiddleSubjects;
        if (!catalog.Contains(Subject))
            Subject = "Social Studies"; // present in both catalogs
        if (!Bands.All.Any(b => b.Key == Band))
            Band = "on";
        if (Bilingual is not null && Bilingual.Trim().Length == 0)
            Bilingual = null;
        CameraIndex = Math.Max(0, CameraIndex);
    }
}

public sealed class ProfileStore
{
    public const string FileName = "kiosk-profiles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public List<KioskProfile> Profiles { get; set; } = [];

    /// <summary>Name of the profile applied at startup, or null to open
    /// with whatever the last session used.</summary>
    public string? Active { get; set; }

    [JsonIgnore]
    public KioskProfile? ActiveProfile =>
        Active is null ? null : Profiles.FirstOrDefault(p => p.Name == Active);

    public static ProfileStore Load(string path = FileName)
    {
        try
        {
            if (!File.Exists(path)) return new ProfileStore();
            var store = JsonSerializer.Deserialize<ProfileStore>(File.ReadAllText(path), JsonOptions)
                        ?? new ProfileStore();
            foreach (var profile in store.Profiles)
                profile.Sanitize();
            // Drop nameless entries and later duplicates of the same name.
            store.Profiles = store.Profiles
                .Where(p => p.Name.Length > 0)
                .GroupBy(p => p.Name)
                .Select(g => g.First())
                .ToList();
            if (store.ActiveProfile is null)
                store.Active = null;
            return store;
        }
        catch
        {
            // Corrupt file: start empty rather than crash the kiosk.
            return new ProfileStore();
        }
    }

    /// <summary>Best-effort save; a read-only folder just means profiles
    /// won't persist (never fatal). Returns false on failure.</summary>
    public bool Save(string path = FileName)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
