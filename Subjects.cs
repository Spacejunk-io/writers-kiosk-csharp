// Writer's Kiosk (C#) — school-level & subject catalog with per-subject
// feedback guidance injected into the system prompt.
// GPL-3.0-or-later; see LICENSE.
using System.Text.Json;

namespace WritersKiosk;

public static class Subjects
{
    public const string Middle = "middle";
    public const string High = "high";

    public static readonly string[] MiddleSubjects =
    [
        "English Language Arts (ELA) / Reading",
        "Mathematics",
        "Science",
        "Social Studies",
        "Health and Physical Education",
        "Visual Arts",
        "Music",
        "Technology Education / Family and Consumer Sciences",
        "World Languages",
    ];

    public static readonly string[] HighSubjects =
    [
        "English",
        "Mathematics",
        "Science",
        "Social Studies",
        "Health and Physical Education",
        "Fine Arts",
        "World Languages",
        "Technology / Career and Technical Education (CTE)",
    ];

    /// <summary>
    /// What adroit feedback looks like in each subject, per level. This
    /// text is injected verbatim into the system prompt's SUBJECT FOCUS.
    /// </summary>
    public static string Guidance(string level, string subject) => (level, subject) switch
    {
        (Middle, "English Language Arts (ELA) / Reading") =>
            "Focus on reading comprehension and written communication: a clear main idea or claim, textual evidence with page or paragraph references, organized paragraphs with transitions, and growing control of grammar and word choice. The Accuracy Check covers misread plot points, misquoted text, and misused vocabulary.",
        (Middle, "Mathematics") =>
            "The student's writing is usually shown work and written explanations — grade-level math through Algebra I. Praise clear representations and correct vocabulary (equation, variable, ratio); push for justified steps (\"how do you know?\"), labeled units, and precise notation. The Accuracy Check verifies computations, units, and every claimed rule or property.",
        (Middle, "Science") =>
            "Integrated middle-grades science writing: claims supported by observations or data, cause-and-effect mechanisms, correct science vocabulary, and fair-test thinking in lab write-ups. The Accuracy Check verifies scientific facts, magnitudes, units, and whether conclusions actually follow from the cited data.",
        (Middle, "Social Studies") =>
            "World history, geography, and U.S. history writing: a clear claim, supporting details with named sources (maps, graphs, documents, labels like \"map A\" or \"L01-E3\"), the cause-and-effect mechanism connecting evidence to claim, and a qualification or limit of the evidence. The Accuracy Check verifies names, dates, places, and magnitudes.",
        (Middle, "Health and Physical Education") =>
            "Health, fitness, and wellness writing: accurate health concepts, realistic personal goals with measurable steps, and reasoning that connects choices to outcomes. The Accuracy Check verifies health facts and figures (nutrition numbers, exercise guidelines) against accepted guidance.",
        (Middle, "Visual Arts") =>
            "Writing about art — artist statements, critiques, and reflections: art vocabulary used correctly (composition, value, contrast), specific observations before judgments, and opinions connected to visual evidence in the work. The Accuracy Check covers misused terms and misattributed artists, works, or movements.",
        (Middle, "Music") =>
            "Writing about music — reflections, concert reports, and theory explanations: correct musical vocabulary (tempo, dynamics, melody), specific listening observations, and connections between musical choices and their effect. The Accuracy Check covers misused terms and wrong composers, eras, or theory claims.",
        (Middle, "Technology Education / Family and Consumer Sciences") =>
            "Introductory STEM, computing, and life-skills writing: clear step-by-step procedures, correct technical vocabulary, and reasoning about design choices, safety, or budgeting. The Accuracy Check verifies technical claims, measurements, and any figure or step that could mislead if wrong.",
        (Middle, "World Languages") =>
            "Introductory modern-language writing: celebrate communication over perfection; look at spelling and diacritics, basic verb conjugation and agreement, vocabulary use, and sentence variety appropriate to a beginner. The Accuracy Check gently notes recurring grammar or spelling patterns and any wrong statement about the language or its cultures.",

        (High, "English") =>
            "English 9-12 through AP Language and AP Literature: a defensible thesis, well-chosen textual evidence integrated with analysis rather than summary, sophisticated organization and transitions, and rhetorical or literary terminology used precisely. The Accuracy Check covers misquoted or misattributed text, misused terms, and misread passages.",
        (High, "Mathematics") =>
            "Algebra I through Pre-Calculus, AP Statistics, and AP Calculus: written work is justification — praise precise notation and definitions; push for logical completeness of each step, stated theorems or properties, units and reasonableness checks, and interpretation of results in context (especially statistics). The Accuracy Check verifies computations, notation, and every claimed rule.",
        (High, "Science") =>
            "Earth Systems, Living Systems (Biology), IPC, Chemistry, Physics, and AP sciences: claim-evidence-reasoning with quantitative support, correct significant figures and units, correlation distinguished from causation, and error analysis in lab reports. The Accuracy Check verifies facts, formulas, magnitudes, and whether conclusions follow from the data presented.",
        (High, "Social Studies") =>
            "U.S. Government, World History, and U.S. History: a defensible thesis, corroborated evidence from named sources, contextualization, cause-and-effect reasoning, and acknowledgment of counterarguments or source limits. The Accuracy Check verifies names, dates, statutes, court cases, and magnitudes.",
        (High, "Health and Physical Education") =>
            "Lifetime fitness and health writing: evidence-based health reasoning, measurable goal-setting, and evaluation of sources behind health claims. The Accuracy Check verifies health and physiology facts and flags pseudo-scientific claims for a source check.",
        (High, "Fine Arts") =>
            "Visual arts, theater, music, and dance writing: precise discipline vocabulary, analysis that ties artistic choices to effect and intent, and critique grounded in observable evidence in the work or performance. The Accuracy Check covers misused terminology and misattributed artists, works, styles, or periods.",
        (High, "World Languages") =>
            "Spanish, French, and other languages: assess communicative success first, then accuracy of tense and mood, agreement, idiomatic usage, and register appropriate to the course level. The Accuracy Check summarizes recurring grammar patterns and flags wrong claims about the language or its cultures.",
        (High, "Technology / Career and Technical Education (CTE)") =>
            "Business, agriculture science, IT/computer science, engineering, and interactive media writing: clear technical documentation, defined terms, justified design or business decisions, and audience-appropriate precision. The Accuracy Check verifies technical claims, calculations, standards, and code or process descriptions.",

        _ => "Give thoughtful, subject-appropriate feedback on the writing's clarity, evidence, organization, and accuracy.",
    };
}

/// <summary>
/// Response bands calibrate the feedback's language and expectations to
/// the individual student a teacher knows — phrased respectfully and
/// asset-based ("Emerging", never "significantly below").
/// </summary>
public static class Bands
{
    public static readonly (string Key, string Display)[] All =
    [
        ("emerging", "Emerging — building foundations"),
        ("approaching", "Approaching grade level"),
        ("on", "On grade level"),
        ("exceeding", "Exceeding grade level"),
        ("advanced", "Advanced — well beyond grade level"),
    ];

    public static string Display(string key) =>
        All.FirstOrDefault(b => b.Key == key).Display ?? "On grade level";

    public static string Guidance(string key) => key switch
    {
        "emerging" =>
            "This student is building foundational writing skills. Use short, simple sentences and the most common words; explain one idea per sentence. Never use a subject term without a plain-word explanation in the same sentence. Be generous and specific in praising genuine effort and every correct element. Keep each feedback item to one or two sentences, and make each Polish step one small, concrete, immediately doable action. The goal is confidence plus one or two real improvements — never a wall of correction.",
        "approaching" =>
            "This student is working toward grade level. Use clear, friendly sentences on the simpler side of grade-level text, and briefly define subject terms when first used. Make Polish steps small and scaffolded — say exactly where in the work and how to try each one. Encourage generously and prioritize the improvements with the biggest payoff.",
        "exceeding" =>
            "This student is working above grade level. Use full academic vocabulary, hold the work to high standards, and let the Questions target nuance — counterarguments, precision of language, deeper connections across the subject. Praise should name what is sophisticated about the work, not merely what is correct.",
        "advanced" =>
            "This student is working well beyond grade level. Respond as to a young scholar: precise disciplinary vocabulary, exacting standards, Questions that probe subtleties an expert would raise, and Polish steps that push toward the conventions of the discipline itself (historiography, formal proof style, literary criticism, publication-quality lab writing). Do not inflate praise — earned, specific recognition only.",
        _ =>
            "This student is working at grade level. Use age-appropriate academic language and normal grade-level expectations.",
    };
}

/// <summary>
/// Live, teacher-adjustable session state: school level, subject, grade,
/// response band, bilingual language, and today's assignment context.
/// Everything except the assignment persists across restarts in a small
/// non-secret JSON file; the assignment persists in assignment.txt.
/// </summary>
public sealed class SessionSettings
{
    private const string UiFile = "kiosk-ui.json";

    public string Level { get; set; } = Subjects.Middle;
    public string Subject { get; set; } = "Social Studies";
    /// <summary>Specific grade, 6-12; kept consistent with Level.</summary>
    public int Grade { get; set; } = 8;
    /// <summary>Response band key from <see cref="Bands"/>.</summary>
    public string Band { get; set; } = "on";
    /// <summary>Home/partner language for bilingual feedback (ELL
    /// support), or null for English-only. Independent of subject.</summary>
    public string? BilingualLanguage { get; set; }
    public string? AssignmentContext { get; set; }

    public string LevelPhrase => Level == Subjects.High ? "high school" : "middle school";
    public string GradePhrase => Level == Subjects.High ? "high school (grades 9-12)" : "middle school (grades 6-8)";
    public string SubjectGuidance => Subjects.Guidance(Level, Subject);
    public string BandDisplay => Bands.Display(Band);
    public string BandGuidance => Bands.Guidance(Band);

    public static SessionSettings Load(KioskConfig cfg)
    {
        var settings = new SessionSettings { AssignmentContext = cfg.AssignmentContext };
        try
        {
            if (File.Exists(UiFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(UiFile));
                var root = doc.RootElement;
                var level = root.GetProperty("level").GetString();
                var subject = root.GetProperty("subject").GetString();
                var catalog = level == Subjects.High ? Subjects.HighSubjects : Subjects.MiddleSubjects;
                if ((level is Subjects.Middle or Subjects.High) && subject is not null && catalog.Contains(subject))
                {
                    settings.Level = level!;
                    settings.Subject = subject;
                }
                if (root.TryGetProperty("grade", out var g) &&
                    g.TryGetInt32(out var grade) && grade is >= 6 and <= 12)
                    settings.Grade = grade;
                if (root.TryGetProperty("band", out var b) &&
                    b.GetString() is { } band && Bands.All.Any(x => x.Key == band))
                    settings.Band = band;
                if (root.TryGetProperty("bilingual", out var bl) &&
                    bl.GetString() is { Length: > 0 } lang)
                    settings.BilingualLanguage = lang;
                // Keep grade consistent with level after loading.
                settings.Grade = settings.Level == Subjects.High
                    ? Math.Clamp(settings.Grade, 9, 12)
                    : Math.Clamp(settings.Grade, 6, 8);
            }
        }
        catch { /* corrupt UI file: fall back to defaults */ }
        return settings;
    }

    public void SaveUiState()
    {
        try
        {
            File.WriteAllText(UiFile, JsonSerializer.Serialize(new
            {
                level = Level,
                subject = Subject,
                grade = Grade,
                band = Band,
                bilingual = BilingualLanguage,
            }));
        }
        catch { /* read-only folder: selection just won't persist */ }
    }
}
