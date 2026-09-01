// Writer's Kiosk (C#) — mandated-reporting support. GPL-3.0-or-later.
//
// Design principles (in order):
// 1. The human educator is the mandated reporter — this code only makes
//    sure adults KNOW, promptly and un-droppably. An alert here is an
//    internal notification, never the legal report itself.
// 2. Alerts carry NO student content and NO identity — the kiosk knows
//    neither. The physical page, in the room with the supervising
//    teacher, is the record.
// 3. The student sees only the same calm notice style as any refusal
//    ("Please bring this page to your teacher") — nothing stigmatizing
//    at a public station.
// 4. District routing is owned by the district: the kiosk POSTs minimal
//    metadata to a BCPS-managed Power Automate (or similar) flow URL,
//    and THAT flow emails the school's teacher-of-record, designated
//    counselor, and principal lists. Recipients live with IT, not here.
// 5. Everything degrades safely: with no flow URL configured, the alert
//    still reaches the console and the local safety log.
using System.Text;
using System.Text.Json;

namespace WritersKiosk;

public static class SafetyAlert
{
    // Same outbound policy as the model call: a redirect is never
    // followed. The payload is metadata only, but it still goes only
    // where IT pointed it; a 3xx surfaces below as a failed POST.
    private static readonly HttpClient Http =
        new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Records and (if configured) forwards a possible-safety-concern
    /// event. Metadata only: station, time, subject mode. Never throws.
    /// </summary>
    public static async Task RaiseAsync(KioskConfig cfg, string subject)
    {
        var stamp = DateTime.Now;
        KioskLog.CountSafety();
        KioskLog.Warn(
            $"SAFETY NOTICE {stamp:h:mm tt}: a submission was flagged as a possible safety concern. " +
            "The student was asked to bring the page to the teacher. Follow school protocol. " +
            "(No content or identity is stored or transmitted.)");

        // Local metadata log — lets the teacher confirm afterwards that
        // the event was raised, without retaining any content.
        try
        {
            Directory.CreateDirectory(FeedbackLog.Folder);
            File.AppendAllText(
                Path.Combine(FeedbackLog.Folder, "safety-log.md"),
                $"- {stamp:yyyy-MM-dd h:mm tt} — possible safety concern flagged at station \"{cfg.StationName}\" " +
                $"({subject} mode). No content retained; the supervising teacher holds the page.\n");
        }
        catch { }

        if (cfg.SafetyWebhookUrl is null)
        {
            KioskLog.Info("(No SAFETY_WEBHOOK_URL configured — no staff notification was sent beyond this log.)");
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                source = "writers-kiosk",
                kind = "possible-safety-concern",
                station = cfg.StationName,
                subjectMode = subject,
                time = stamp.ToString("o"),
                note = "No student content or identity is included. The supervising teacher at the station holds the physical page.",
            });
            var response = await Http.PostAsync(
                cfg.SafetyWebhookUrl,
                new StringContent(payload, Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
                KioskLog.Info("Safety alert delivered to the district notification flow.");
            else
                KioskLog.Warn($"Safety alert POST returned {(int)response.StatusCode} — notify staff manually.");
        }
        catch (Exception ex)
        {
            KioskLog.Warn($"Safety alert could not be delivered ({ex.Message}) — notify staff manually.");
        }
    }
}
