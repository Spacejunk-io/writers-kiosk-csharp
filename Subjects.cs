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
/// Live, teacher-adjustable session state: school level, subject, and
/// today's assignment context. Level/subject persist across restarts in
/// a small non-secret JSON file; the assignment persists in
/// assignment.txt as before.
/// </summary>
public sealed class SessionSettings
{
    private const string UiFile = "kiosk-ui.json";

    public string Level { get; set; } = Subjects.Middle;
    public string Subject { get; set; } = "Social Studies";
    public string? AssignmentContext { get; set; }

    public string LevelPhrase => Level == Subjects.High ? "high school" : "middle school";
    public string GradePhrase => Level == Subjects.High ? "high school (grades 9-12)" : "middle school (grades 6-8)";
    public string SubjectGuidance => Subjects.Guidance(Level, Subject);

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
            }
        }
        catch { /* corrupt UI file: fall back to defaults */ }
        return settings;
    }

    public void SaveUiState()
    {
        try
        {
            File.WriteAllText(UiFile, JsonSerializer.Serialize(new { level = Level, subject = Subject }));
        }
        catch { /* read-only folder: selection just won't persist */ }
    }
}
