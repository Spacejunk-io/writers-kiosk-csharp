// Writer's Kiosk (C#) — keyless Entra ID auth for Azure OpenAI.
// GPL-3.0-or-later; see LICENSE.
//
// "Rung 3": instead of an API key in .env, the kiosk authenticates as
// the signed-in district account. The first launch opens a browser
// sign-in once; the tokens land in an MSAL cache encrypted with Windows
// DPAPI (readable only by this Windows user on this machine), plus a
// small non-secret account record, so every later launch is silent.
// Requests then carry a short-lived bearer token — no long-lived secret
// ever exists on disk, and IT revokes access by disabling the account
// or removing its "Cognitive Services OpenAI User" role.
using Azure.Core;
using Azure.Identity;

namespace WritersKiosk;

public sealed class EntraTokenProvider
{
    private static readonly string[] Scopes = ["https://cognitiveservices.azure.com/.default"];

    private readonly InteractiveBrowserCredential _credential;
    private readonly string _recordPath;
    private AccessToken _token;

    public EntraTokenProvider(string? tenantId, string? clientId)
    {
        _recordPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "writers-kiosk", "entra-account.json");

        var options = new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            // null falls back to the Azure SDK's public client id; a
            // district can register its own app and set AZURE_CLIENT_ID.
            ClientId = clientId,
            // Only sign in interactively when we explicitly ask to, so a
            // token refresh can never surprise a classroom with a browser.
            DisableAutomaticAuthentication = true,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = "writers-kiosk", // DPAPI-encrypted on Windows
            },
        };

        // The account record (which account to use — not a secret) lets
        // later runs resume the cached sign-in silently.
        try
        {
            if (File.Exists(_recordPath))
            {
                using var stream = File.OpenRead(_recordPath);
                options.AuthenticationRecord = AuthenticationRecord.Deserialize(stream);
            }
        }
        catch { /* unreadable record: fall through to a fresh sign-in */ }

        _credential = new InteractiveBrowserCredential(options);
    }

    /// <summary>
    /// Returns a valid bearer token, signing in interactively only if the
    /// silent cache cannot supply one (first run, revoked session, or an
    /// expired refresh token).
    /// </summary>
    public async Task<string> GetTokenAsync()
    {
        if (_token.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            return _token.Token;

        var context = new TokenRequestContext(Scopes);
        try
        {
            _token = await _credential.GetTokenAsync(context);
        }
        catch (AuthenticationRequiredException)
        {
            Console.WriteLine("[kiosk] District sign-in needed — check the browser window that just opened…");
            var record = await _credential.AuthenticateAsync(context);
            Directory.CreateDirectory(Path.GetDirectoryName(_recordPath)!);
            using (var stream = File.Create(_recordPath))
                record.Serialize(stream);
            _token = await _credential.GetTokenAsync(context);
            Console.WriteLine($"[kiosk] Signed in as {record.Username}. Future launches will be silent.");
        }
        return _token.Token;
    }
}
