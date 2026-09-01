// Writer's Kiosk (C#) — classroom document-camera feedback kiosk.
// Copyright (C) 2026 Spacejunk-io — George Bacon <spacejunk572@gmail.com>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version. See the LICENSE file.

namespace WritersKiosk;

public enum Provider { OpenAi, Azure }

public sealed class KioskConfig
{
    public Provider Provider { get; private init; }
    public string ApiKey { get; private init; } = "";
    public string Model { get; private init; } = "gpt-4o";
    public string AzureEndpoint { get; private init; } = "";
    public string AzureDeployment { get; private init; } = "";
    public string AzureApiVersion { get; private init; } = "2024-06-01";
    /// <summary>True = keyless Entra ID sign-in ("Rung 3"); false = api-key header.</summary>
    public bool AzureUseEntra { get; private init; }
    public string? AzureTenantId { get; private init; }
    public string? AzureClientId { get; private init; }

    public int CameraIndex { get; private init; }
    public string? SumatraPath { get; private init; }
    public string? PrinterName { get; private init; }
    /// <summary>null = single-sided, "long" or "short" = duplex flip edge.</summary>
    public string? Duplex { get; private init; }
    public Point? WindowPos { get; private init; }
    public string? AssignmentContext { get; private init; }
    /// <summary>Where the in-app assignment editor saves (ASSIGNMENT_FILE).</summary>
    public string AssignmentFile { get; private init; } = "assignment.txt";
    public bool FlipVertical { get; private init; }
    public bool FlipHorizontal { get; private init; }
    public bool Enhance { get; private init; }
    public int CooldownSeconds { get; private init; }
    /// <summary>Append each report's text to feedback-log\ for teacher review (default on).</summary>
    public bool FeedbackLogEnabled { get; private init; }
    /// <summary>District notification flow URL (e.g. Power Automate) for
    /// safety alerts; unset = console + local safety log only.</summary>
    public string? SafetyWebhookUrl { get; private init; }
    /// <summary>Human-readable station identity used in safety alerts.</summary>
    public string StationName { get; private init; } = "";

    /// <summary>
    /// Every outbound URL the kiosk is configured with must be absolute
    /// https with no credentials in it. The model endpoint carries student
    /// page images and the district flow carries safety metadata; a
    /// mistyped http:// would send either in the clear, silently.
    /// </summary>
    internal static string HttpsUrl(string name, string value, bool allowQuery)
    {
        var ok = Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                 uri.Scheme == Uri.UriSchemeHttps &&
                 uri.UserInfo.Length == 0 &&
                 uri.Fragment.Length == 0 &&
                 (allowQuery || uri.Query.Length == 0);
        if (!ok)
            throw new InvalidOperationException(
                $"{name} must be an absolute https:// URL with no credentials" +
                (allowQuery ? "" : " or query string") + $", got \"{value}\".");
        return value;
    }

    public static KioskConfig Load()
    {
        // Missing .env is fine — variables may be set at the OS level.
        try { DotNetEnv.Env.Load(); } catch { }

        string Get(string name, string fallback = "") =>
            Environment.GetEnvironmentVariable(name)?.Trim() ?? fallback;

        string Require(string name)
        {
            var v = Get(name);
            if (v.Length == 0 || v.Contains("REPLACE_ME"))
                throw new InvalidOperationException(
                    $"{name} is not set (or still holds the placeholder). Copy .env.example to .env and fill it in.");
            return v;
        }

        bool Flag(string name) =>
            Get(name).ToLowerInvariant() is "1" or "true" or "yes" or "on";

        var providerName = Get("LLM_PROVIDER", "openai").ToLowerInvariant();
        var provider = providerName switch
        {
            "openai" => Provider.OpenAi,
            "azure" => Provider.Azure,
            _ => throw new InvalidOperationException(
                $"LLM_PROVIDER must be \"openai\" or \"azure\", got \"{providerName}\""),
        };

        Point? windowPos = null;
        if (int.TryParse(Get("WINDOW_X"), out var wx) && int.TryParse(Get("WINDOW_Y"), out var wy))
            windowPos = new Point(wx, wy);

        string? assignment = null;
        var assignmentFile = Get("ASSIGNMENT_FILE", "assignment.txt");
        try
        {
            if (File.Exists(assignmentFile))
            {
                var text = File.ReadAllText(assignmentFile).Trim();
                if (text.Length > 0) assignment = text;
            }
        }
        catch { }

        // Azure auth style: an explicit AZURE_AUTH wins; otherwise keyless
        // Entra sign-in whenever no API key is configured.
        var azureKey = Get("AZURE_OPENAI_API_KEY");
        var azureUseEntra = provider == Provider.Azure && Get("AZURE_AUTH").ToLowerInvariant() switch
        {
            "entra" or "keyless" => true,
            "key" => false,
            _ => azureKey.Length == 0 || azureKey.Contains("REPLACE_ME"),
        };
        if (provider == Provider.Azure && !azureUseEntra)
            azureKey = Require("AZURE_OPENAI_API_KEY");

        return new KioskConfig
        {
            Provider = provider,
            ApiKey = provider == Provider.OpenAi ? Require("OPENAI_API_KEY") : azureKey,
            Model = Get("OPENAI_MODEL", "gpt-4o"),
            AzureEndpoint = provider == Provider.Azure
                ? HttpsUrl("AZURE_OPENAI_ENDPOINT", Require("AZURE_OPENAI_ENDPOINT"), allowQuery: false).TrimEnd('/')
                : "",
            AzureDeployment = provider == Provider.Azure ? Require("AZURE_OPENAI_DEPLOYMENT") : "",
            AzureApiVersion = Get("AZURE_OPENAI_API_VERSION", "2024-06-01"),
            AzureUseEntra = azureUseEntra,
            AzureTenantId = Get("AZURE_TENANT_ID") is { Length: > 0 } tid ? tid : null,
            AzureClientId = Get("AZURE_CLIENT_ID") is { Length: > 0 } cid ? cid : null,
            CameraIndex = int.TryParse(Get("CAMERA_INDEX"), out var ci) ? ci : 0,
            SumatraPath = Get("SUMATRA_PATH") is { Length: > 0 } sp ? sp : null,
            PrinterName = Get("PRINTER_NAME") is { Length: > 0 } pn ? pn : null,
            Duplex = Get("PRINT_DUPLEX").ToLowerInvariant() switch
            {
                "" or "0" or "off" or "false" or "no" => null,
                "short" => "short",
                _ => "long", // "long", "1", "true", "on", "yes"
            },
            WindowPos = windowPos,
            AssignmentContext = assignment,
            AssignmentFile = assignmentFile,
            FlipVertical = Flag("FLIP_VERTICAL"),
            FlipHorizontal = Flag("FLIP_HORIZONTAL"),
            Enhance = Get("ENHANCE").ToLowerInvariant() is not ("0" or "false" or "off" or "no"),
            CooldownSeconds = int.TryParse(Get("COOLDOWN_SECONDS"), out var cd) ? cd : 8,
            FeedbackLogEnabled = Get("FEEDBACK_LOG").ToLowerInvariant() is not ("0" or "false" or "off" or "no"),
            // A Power Automate trigger URL carries its SAS signature in the
            // query string, so the query is allowed here and only here.
            SafetyWebhookUrl = Get("SAFETY_WEBHOOK_URL") is { Length: > 0 } wh
                ? HttpsUrl("SAFETY_WEBHOOK_URL", wh, allowQuery: true)
                : null,
            StationName = Get("STATION_NAME", Environment.MachineName),
        };
    }
}
