// Writer's Kiosk tests — outbound policy for the model call. GPL-3.0-or-later.
using System.Net;
using Xunit;

namespace WritersKiosk.Tests;

/// <summary>
/// The request body carries the student's page images. These tests pin
/// what keeps it going only where it was sent: the handler never follows
/// a redirect, and a redirect response is reported as a failure after
/// exactly one request rather than acted on.
/// </summary>
[Collection("environment")] // builds a KioskConfig from environment variables
public sealed class LlmClientEgressTests
{
    [Fact]
    public void TheOutboundHandlerNeverFollowsRedirects()
    {
        using var handler = LlmClient.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task ARedirectIsRefusedAfterExactlyOneRequest()
    {
        var stub = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://elsewhere.example/collect");
            return response;
        });
        var ex = await CallThrough(stub);
        Assert.Contains("redirect", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, stub.Requests);
    }

    [Fact]
    public async Task ANonJsonErrorBodyStillReportsTheStatusCode()
    {
        // A proxy's HTML error page must not surface as a JSON parse error.
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("<html><body>400 Bad Request</body></html>"),
        });
        var ex = await CallThrough(stub);
        Assert.Contains("400", ex.Message);
        Assert.Equal(1, stub.Requests); // 4xx is not retried
    }

    /// <summary>Routes one feedback call through the stub and returns the
    /// failure it produced, restoring the real client afterwards.</summary>
    private static async Task<InvalidOperationException> CallThrough(StubHandler stub)
    {
        var original = LlmClient.Http;
        LlmClient.Http = new HttpClient(stub);
        try
        {
            var cfg = KioskConfigTests.LoadWith(("LLM_PROVIDER", "openai"), ("OPENAI_API_KEY", "sk-test"));
            var session = new SessionSettings { Subject = "Science" };
            return await Assert.ThrowsAsync<InvalidOperationException>(() =>
                LlmClient.GetFeedbackAsync(cfg, [new byte[] { 0xFF, 0xD8, 0xFF }], session));
        }
        finally
        {
            LlmClient.Http.Dispose();
            LlmClient.Http = original;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }
}
