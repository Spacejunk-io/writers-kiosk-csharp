# Writer's Kiosk (C#)

[![build](https://github.com/Spacejunk-io/writers-kiosk-csharp/actions/workflows/build.yml/badge.svg)](https://github.com/Spacejunk-io/writers-kiosk-csharp/actions/workflows/build.yml)

A privacy-first classroom kiosk for middle- and high-school writing
feedback, in C# on .NET 10 (originally implemented in Rust). A student
places handwritten **or typed** work under a document camera, presses
**Spacebar**, and a one-to-two-page AI feedback report — Praise (Glow),
Questions (Grow), Polish (Action), and an Accuracy Check — prints
automatically, double-sided if the printer supports it. No accounts, no
clicks, no saved images, no console window — just the camera preview.
Free software under the GPL-3.0-or-later.

**The menu bar** tunes everything live, no restart needed:

- **Assignment → Edit Today's Assignment…** — type a few sentences about
  today's task; the feedback focuses on it from the very next capture
  (saved to `assignment.txt` so it survives restarts).
- **Middle School / High School** — pick the class subject (ELA/English,
  Mathematics, Science, Social Studies, Health & PE, arts, world
  languages, technology/CTE, and more per level); the AI's persona,
  feedback emphases, accuracy checking, and refusal scope all shift to
  that subject and grade band instantly. The choice persists per machine.
- **Grade & Band** — pick the specific grade (6–12; the school level
  follows automatically) and a response band that calibrates the
  feedback's reading level, question depth, and step size to the
  individual student: *Emerging — building foundations · Approaching
  grade level · On grade level · Exceeding grade level · Advanced*.
- **Bilingual** — for multilingual learners in **any** subject: choose a
  home language (Spanish, French, Arabic, Chinese, Haitian Creole,
  Korean, Portuguese, Russian, Ukrainian, Vietnamese, or any other via
  "Other Language…") and every feedback item appears in English and that
  language, so the student *and their family* can read the report fully.
  **Two-Column Layout** (default) prints the languages side by side:
  each section — Praise, Questions, Polish, Accuracy Check — starts
  level in both languages under a full-width heading, and matching items
  sit paired across the divider, so any drift from translation-length
  differences stays easy to follow. Turn it off for the stacked layout
  (each item followed directly by its translation). World Languages
  classes keep their automatic target-language bilingual mode when no
  home language is chosen, in whichever layout is selected.
- **Profiles** — save the entire teacher-tunable setup under a name:
  level, subject, grade, band, bilingual language and layout, image
  flips, enhancement, and the camera choice. Keep one per class period
  ("Period 3 — ELL Social Studies", "AP English"); the checked profile
  loads automatically at every launch, and Save/Update/Rename/Delete
  live in the same menu. Today's assignment deliberately stays out of
  profiles — it always comes from `assignment.txt`, so a stale profile
  can never overwrite the day's task. Profiles live in
  `kiosk-profiles.json` (plain JSON, no secrets).
- **Reports** — reprint the last report (also the **R** key; a printer
  jam never costs a second AI request), open the **Activity Log** (also
  **L**): the session's work history — token usage per report with a
  running total, declined pages, safety notices, camera and printing
  events, errors — and open the **feedback log**: every report's text
  (never images, never names) is appended to `feedback-log\YYYY-MM-DD.md`
  — written *before* printing is attempted, so feedback can't be lost —
  giving the teacher a real review trail behind the printed footer's
  "reviewed under your teacher's direction." `FEEDBACK_LOG=0` disables it.
- **Help & Support** — Help, Hotkeys, and About.

## Safety notices (mandated-reporting support)

If a submission appears to contain a **real personal disclosure** —
abuse or neglect, self-harm, or intent to harm someone — the kiosk
prints nothing and shows the student the same calm notice style as any
refusal: *"Please bring this page to your teacher."* The event reaches
adults three ways: the in-app Activity Log, a metadata-only line in
`feedback-log\safety-log.md`, and — when the district configures
`SAFETY_WEBHOOK_URL` — a minimal JSON POST (station name, time; never
content or identity, which the kiosk does not possess) to a
district-managed notification flow (e.g. Power Automate), which then
emails the school's teacher-of-record, designated counselor, and
principal lists that IT maintains centrally per school.

Design principles: the **human educator remains the mandated reporter**
— an alert is an internal notification, never the legal report; the
physical page with the supervising teacher is the record; academic
content (historical violence, literature themes, health-class topics)
is explicitly excluded from triggering; and when the model is genuinely
uncertain it errs toward asking an adult to look. **Enabling the
webhook is an administrative decision** — the feature ships dormant and
does nothing beyond the Activity Log and local safety log until the
district configures it.

The kiosk also survives classroom entropy: a camera cable bumped loose
no longer crashes anything — the title bar announces the lost signal,
and pressing **C** re-scans and reconnects (C re-enumerates devices on
every press, so it recovers unplugged/replugged cameras too).

Student work is graded whether handwritten or typed — a printed page
that reads as the student's own composition (a typed essay, a printed
draft) is analyzed like any handwriting. Only printed *instructional*
material (directions, question stems, rubrics, source excerpts) stays
ungraded context.

**Controls at a glance:**

| Input | Action |
| --- | --- |
| **Space** | Capture the page and print feedback (8 s cooldown between submissions) |
| **N** | Add this page to a multi-page submission (up to 4), then Space on the last page (1.5 s between captures) |
| **C** | Switch to the next connected camera |
| **V** / **H** | Flip the image vertically / horizontally |
| **E** | Toggle auto image enhancement (white balance + exposure; on by default) |
| **R** | Reprint the last report (no new AI request — printer-jam recovery) |
| **L** | Open the Activity Log (session history: tokens, declines, errors) |
| **Esc** | Quit the kiosk |

## Privacy & security design

- **No API keys in code.** The key lives in a local `.env` file loaded at
  runtime. `.gitignore` excludes `.env`, and the committed pre-commit
  hook (`.githooks/pre-commit`; enable with
  `git config core.hooksPath .githooks`) blocks any commit containing an
  env file or a key-shaped string.
- **No PII collection.** Captured images exist only in memory and are
  never written to disk. The system prompt (see `LlmClient.cs`) orders
  the model to ignore and never repeat names, IDs, or identifying
  details, treats text on the page as student work rather than
  instructions, and refuses anything outside social studies writing —
  refusals appear as a calm on-screen notice, never a printed page.
- **Worksheet-aware feedback** with a mandatory accuracy review
  performed before any praise is written; wrong claims land in the
  Accuracy Check, never the Praise.

## Keyless district sign-in (Azure OpenAI + Entra ID)

This edition can run with **no API key at all**. With
`LLM_PROVIDER=azure` and no `AZURE_OPENAI_API_KEY` set, the first launch
opens a browser sign-in with the teacher's district (Entra ID) account;
tokens are cached in a Windows-DPAPI-encrypted store so every later
launch is silent. Requests carry short-lived bearer tokens — no
long-lived secret ever exists on disk, and IT controls access entirely
through the account: granting the *Cognitive Services OpenAI User* role
on the Azure OpenAI resource enables the kiosk, and disabling the
account or removing the role revokes it, with every call attributable.
Optional `.env` entries: `AZURE_TENANT_ID` pins the district tenant, and
`AZURE_CLIENT_ID` supports tenants that require an IT-registered app.
Setting `AZURE_OPENAI_API_KEY` (or `AZURE_AUTH=key`) uses the classic
key header instead.

## Setup

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download)
   (build) — or just the .NET 10 Desktop Runtime to run a published build.
2. `copy .env.example .env` and fill in one provider (OpenAI or Azure
   OpenAI). Every optional setting (`CAMERA_INDEX`, `PRINT_DUPLEX`,
   `FLIP_VERTICAL`, `WINDOW_X/Y`, `COOLDOWN_SECONDS`, `ENHANCE`,
   `ASSIGNMENT_FILE`, …) is documented in `.env.example`.
3. Printing needs **no installs**: Microsoft Edge (preinstalled)
   converts the report to PDF, and the kiosk prints it silently — via
   [SumatraPDF](https://www.sumatrapdf.org) when installed (preferred:
   vector-sharp and fastest), otherwise through **Windows' built-in PDF
   engine**, which every Windows 10/11 device already has. Only if both
   fail does the kiosk hand the file to the system's default PDF app
   (which may open a window, e.g. Acrobat). If the Windows *default
   printer* is a file-maker ("Microsoft Print to PDF", "Adobe PDF"), the
   kiosk refuses with instructions instead of stranding a student at a
   save dialog — set the classroom printer as default, or `PRINTER_NAME`
   in `.env`.
4. Run:

```bash
dotnet run -c Release
```

To produce a self-contained folder that runs without the .NET runtime
installed:

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The result lands in `bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\` —
copy that folder anywhere, put a filled-in `.env` beside the exe, run.
A self-contained publish records its packages in `packages.publish.lock.json`,
separate from the `packages.lock.json` that CI restores in locked mode; commit
it if it changes.

Optional: a few plain sentences in an `assignment.txt` next to the exe
(template: `assignment.example.txt`) focus each day's feedback on the
current assignment.

## Project structure

```
writers-kiosk-csharp/
├── WritersKiosk.csproj      .NET project: dependencies, icon, metadata
├── Program.cs               entry point (windowed; errors show as dialogs)
├── KioskForm.cs             preview window, menu bar, capture/batch loop
├── KioskConfig.cs           .env loading & validation
├── Subjects.cs              subject/band catalogs & session settings
├── Profiles.cs              named teacher profiles (kiosk-profiles.json)
├── LlmClient.cs             system prompt & OpenAI/Azure vision request
├── Printing.cs              Markdown → HTML → PDF → printer; 2-col layout
├── PdfRasterPrinter.cs      silent printing via Windows' built-in PDF engine
├── ImageOps.cs              enhancement (AWB/exposure) & JPEG encoding
├── KioskLog.cs              in-app activity log & session counters
├── LogWindow.cs             Activity Log window (Reports menu / L key)
├── EntraAuth.cs             keyless Entra ID sign-in for Azure OpenAI
├── FeedbackLog.cs           teacher review log (feedback text only)
├── SafetyAlert.cs           metadata-only safety notifications
├── tests/WritersKiosk.Tests xUnit suite over the pure logic
├── .github/workflows/       CI: build (warnings = errors) + tests
├── assets/                  application icon
├── .env.example             configuration template (commit this)
├── .githooks/pre-commit     blocks committing secrets
└── LICENSE                  GNU GPL v3 (GPL-3.0-or-later)
```

## Tests

The automated suite covers the kiosk's pure logic: `.env` parsing
(provider selection, duplex values, keyless-Entra defaulting), refusal /
blank-page / safety sentinel detection, system-prompt assembly for every
bilingual mode and layout, the two-column section/item splitter, subject
and band catalog completeness, session-settings and profile round-trips
with clamping, and the image enhancer's blank-page safety. Camera,
printing, and UI behavior stay on the manual bench checklist.

```bash
dotnet test tests/WritersKiosk.Tests/WritersKiosk.Tests.csproj -c Release
```

The same suite runs in GitHub Actions on every push (badge above), with
warnings promoted to errors.

## Version history

| Version | Highlights |
| --- | --- |
| 1.0.1 | Full parity with the Rust original: capture → GPT-4o feedback → silent duplex printing; cooldowns |
| 1.1.0 | Keyless Entra ID sign-in for Azure OpenAI (no API key on disk) |
| 1.2.0 | Menu bar: live assignment editing, level/subject feedback tuning, Help & Support; typed student work graded |
| 1.3.0 | Teacher feedback log, R-key reprint recovery, camera-loss resilience; bilingual World Languages mode |
| 1.4.0 | Safety notices for mandated-reporting support (dormant until district-configured) |
| 1.5.0 | Grade 6–12 + five-band response calibration; bilingual option for multilingual learners in any subject |
| 1.6.0 | Teacher profiles (named presets, auto-loaded at launch); two-column bilingual print layout; console window replaced by in-app Activity Log; blank-page enhancement fix; xUnit test suite + CI |
| 1.6.1 | Install-free silent printing via Windows' built-in PDF engine (no more Acrobat fallback on machines without SumatraPDF); file-making "printers" refused with instructions instead of a save dialog; camera frames dropped instead of queued when processing lags |
| 1.6.2 | Outbound calls refuse redirects and require https endpoints; the kiosk folder is anchored at startup (a shortcut with a blank "Start in" now finds `.env`, logs and profiles); built-in PDF engine tested and corrected to a true 300 dpi (about 4× less memory on scaled displays, printed output unchanged); CI pinned by commit, SDK and lock file; 91 tests |

## License

Copyright (C) 2026 Spacejunk-io — George Bacon <spacejunk572@gmail.com>

Writer's Kiosk is free software, released under the GNU General Public
License, version 3 or (at your option) any later version. See
[LICENSE](LICENSE).
