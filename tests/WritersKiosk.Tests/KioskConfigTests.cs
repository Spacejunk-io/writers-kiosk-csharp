// Writer's Kiosk tests — .env/environment parsing. GPL-3.0-or-later.
using Xunit;

namespace WritersKiosk.Tests;

// Environment variables are process-global, so every test that touches
// them lives in this one non-parallel collection.
[CollectionDefinition("environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;

[Collection("environment")]
public sealed class KioskConfigTests
{
    private static readonly string[] AllVars =
    [
        "LLM_PROVIDER", "OPENAI_API_KEY", "OPENAI_MODEL",
        "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_API_KEY", "AZURE_OPENAI_DEPLOYMENT",
        "AZURE_OPENAI_API_VERSION", "AZURE_AUTH", "AZURE_TENANT_ID", "AZURE_CLIENT_ID",
        "CAMERA_INDEX", "SUMATRA_PATH", "PRINTER_NAME", "PRINT_DUPLEX",
        "WINDOW_X", "WINDOW_Y", "ASSIGNMENT_FILE", "FLIP_VERTICAL", "FLIP_HORIZONTAL",
        "ENHANCE", "COOLDOWN_SECONDS", "FEEDBACK_LOG", "SAFETY_WEBHOOK_URL", "STATION_NAME",
    ];

    /// <summary>Loads a config with exactly the given variables set.</summary>
    internal static KioskConfig LoadWith(params (string Key, string Value)[] vars)
    {
        foreach (var name in AllVars) Environment.SetEnvironmentVariable(name, null);
        foreach (var (key, value) in vars) Environment.SetEnvironmentVariable(key, value);
        try
        {
            return KioskConfig.Load();
        }
        finally
        {
            foreach (var name in AllVars) Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void OpenAiIsTheDefaultProviderWithSaneDefaults()
    {
        var cfg = LoadWith(("OPENAI_API_KEY", "sk-test"));
        Assert.Equal(Provider.OpenAi, cfg.Provider);
        Assert.Equal("sk-test", cfg.ApiKey);
        Assert.Equal("gpt-4o", cfg.Model);
        Assert.Equal(8, cfg.CooldownSeconds);
        Assert.True(cfg.Enhance);
        Assert.True(cfg.FeedbackLogEnabled);
        Assert.Null(cfg.Duplex);
        Assert.Null(cfg.PrinterName);
        Assert.Equal(0, cfg.CameraIndex);
    }

    [Fact]
    public void MissingOpenAiKeyThrows() =>
        Assert.Throws<InvalidOperationException>(() => LoadWith());

    [Fact]
    public void PlaceholderKeyThrows() =>
        Assert.Throws<InvalidOperationException>(() =>
            LoadWith(("OPENAI_API_KEY", "sk-REPLACE_ME")));

    [Fact]
    public void UnknownProviderThrows() =>
        Assert.Throws<InvalidOperationException>(() =>
            LoadWith(("LLM_PROVIDER", "llama"), ("OPENAI_API_KEY", "sk-test")));

    [Theory]
    [InlineData("", null)]
    [InlineData("off", null)]
    [InlineData("no", null)]
    [InlineData("0", null)]
    [InlineData("short", "short")]
    [InlineData("long", "long")]
    [InlineData("1", "long")]
    [InlineData("yes", "long")]
    public void DuplexValuesParse(string value, string? expected)
    {
        var cfg = LoadWith(("OPENAI_API_KEY", "sk-test"), ("PRINT_DUPLEX", value));
        Assert.Equal(expected, cfg.Duplex);
    }

    [Fact]
    public void AzureWithoutKeyDefaultsToKeylessEntra()
    {
        var cfg = LoadWith(
            ("LLM_PROVIDER", "azure"),
            ("AZURE_OPENAI_ENDPOINT", "https://example.openai.azure.com/"),
            ("AZURE_OPENAI_DEPLOYMENT", "gpt-4o"));
        Assert.Equal(Provider.Azure, cfg.Provider);
        Assert.True(cfg.AzureUseEntra);
        Assert.Equal("", cfg.ApiKey);
        // Trailing slash must be trimmed so URL building stays valid.
        Assert.Equal("https://example.openai.azure.com", cfg.AzureEndpoint);
    }

    [Fact]
    public void AzureWithKeyUsesTheKey()
    {
        var cfg = LoadWith(
            ("LLM_PROVIDER", "azure"),
            ("AZURE_OPENAI_ENDPOINT", "https://example.openai.azure.com"),
            ("AZURE_OPENAI_DEPLOYMENT", "gpt-4o"),
            ("AZURE_OPENAI_API_KEY", "abc123"));
        Assert.False(cfg.AzureUseEntra);
        Assert.Equal("abc123", cfg.ApiKey);
    }

    [Fact]
    public void ExplicitAzureAuthEntraOverridesAPresentKey()
    {
        var cfg = LoadWith(
            ("LLM_PROVIDER", "azure"),
            ("AZURE_OPENAI_ENDPOINT", "https://example.openai.azure.com"),
            ("AZURE_OPENAI_DEPLOYMENT", "gpt-4o"),
            ("AZURE_OPENAI_API_KEY", "abc123"),
            ("AZURE_AUTH", "entra"));
        Assert.True(cfg.AzureUseEntra);
    }

    [Theory]
    [InlineData("20", 20)]
    [InlineData("0", 0)]
    [InlineData("abc", 8)]
    [InlineData("", 8)]
    public void CooldownSecondsParse(string value, int expected)
    {
        var cfg = LoadWith(("OPENAI_API_KEY", "sk-test"), ("COOLDOWN_SECONDS", value));
        Assert.Equal(expected, cfg.CooldownSeconds);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("off", false)]
    [InlineData("1", true)]
    [InlineData("", true)]
    public void EnhanceFlagParses(string value, bool expected)
    {
        var cfg = LoadWith(("OPENAI_API_KEY", "sk-test"), ("ENHANCE", value));
        Assert.Equal(expected, cfg.Enhance);
    }

    [Fact]
    public void WindowPositionNeedsBothCoordinates()
    {
        var both = LoadWith(("OPENAI_API_KEY", "sk-test"), ("WINDOW_X", "1920"), ("WINDOW_Y", "0"));
        Assert.Equal(new Point(1920, 0), both.WindowPos);
        var onlyX = LoadWith(("OPENAI_API_KEY", "sk-test"), ("WINDOW_X", "1920"));
        Assert.Null(onlyX.WindowPos);
    }

    // ── Outbound URLs: https only, no credentials ───────────────────

    [Theory]
    [InlineData("http://example.openai.azure.com")]              // cleartext
    [InlineData("https://user:secret@example.openai.azure.com")] // credentials in the URL
    [InlineData("https://example.openai.azure.com/?x=1")]        // the code appends its own query
    [InlineData("example.openai.azure.com")]                     // not absolute
    public void AzureEndpointMustBeABareHttpsUrl(string endpoint) =>
        Assert.Throws<InvalidOperationException>(() => LoadWith(
            ("LLM_PROVIDER", "azure"),
            ("AZURE_OPENAI_ENDPOINT", endpoint),
            ("AZURE_OPENAI_DEPLOYMENT", "gpt-4o")));

    [Fact]
    public void SafetyWebhookMustBeHttpsButMayCarryItsSignatureInTheQuery()
    {
        // A Power Automate trigger URL puts its SAS signature in the query.
        var flow = "https://prod-00.westus.logic.azure.com:443/workflows/abc/triggers/manual/paths/invoke" +
                   "?api-version=2016-06-01&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=REDACTED";
        var cfg = LoadWith(("OPENAI_API_KEY", "sk-test"), ("SAFETY_WEBHOOK_URL", flow));
        Assert.Equal(flow, cfg.SafetyWebhookUrl);

        Assert.Throws<InvalidOperationException>(() => LoadWith(
            ("OPENAI_API_KEY", "sk-test"),
            ("SAFETY_WEBHOOK_URL", "http://prod-00.westus.logic.azure.com/workflows/abc")));
    }
}
