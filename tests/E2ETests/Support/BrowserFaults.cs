using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chromium;

namespace E2ETests.Support;

// Makes one browser request fail without touching the backend, for tests of how a screen
// copes with a single failing API call. Every frontend API call goes through fetch
// (frontend/src/api/client.ts), so fetch is wrapped before any page script runs. Matching
// requests get the given status; everything else still reaches the real Gateway. A single
// DevTools command is used rather than Selenium's network interception, whose event
// handling is tied to specific Chrome versions.
public static class BrowserFaults
{
    // Applies to every document loaded afterwards in this browser tab.
    public static void FailRequests(IWebDriver driver, string pathSuffix, string method = "GET", int status = 500)
    {
        var chromium = driver as ChromiumDriver
            ?? throw new InvalidOperationException("Browser fault injection needs a Chromium-based driver.");

        var script = $$"""
            (() => {
              const pathSuffix = {{JsonSerializer.Serialize(pathSuffix)}};
              const method = {{JsonSerializer.Serialize(method.ToUpperInvariant())}};
              const originalFetch = window.fetch.bind(window);
              window.fetch = (input, init) => {
                const isRequest = typeof input !== 'string' && !(input instanceof URL);
                const url = isRequest ? input.url : String(input);
                const requestMethod = ((init && init.method) || (isRequest ? input.method : 'GET')).toUpperCase();
                if (requestMethod === method && new URL(url, window.location.href).pathname.endsWith(pathSuffix)) {
                  return Promise.resolve(new Response(
                    JSON.stringify({ message: 'Simulated E2E failure' }),
                    { status: {{status}}, headers: { 'Content-Type': 'application/json' } }));
                }
                return originalFetch(input, init);
              };
            })();
            """;

        chromium.ExecuteCdpCommand(
            "Page.addScriptToEvaluateOnNewDocument",
            new Dictionary<string, object> { ["source"] = script });
    }
}
