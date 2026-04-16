using System;
using System.Net.Http;
using System.Diagnostics;

namespace NetworkDetector;

public static class HttpProbe
{
    // Endpoints that return a known, verifiable response
    private static readonly string[] TestUrls =
    [
        "http://connectivitycheck.gstatic.com/generate_204",
        "http://cp.cloudflare.com/",
    ];

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public static NetworkResult? TryDetect()
    {
        foreach (var url in TestUrls)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var handler = new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseProxy = false,
                    DefaultProxyCredentials = null,
                };
                using var client = new HttpClient(handler) { Timeout = Timeout };

                var response = client.GetAsync(url).GetAwaiter().GetResult();
                sw.Stop();

                // Verify actual response body to guard against proxy returning fake status
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var statusCode = (int)response.StatusCode;

                // gstatic returns empty 204; cloudflare returns a short HTML page
                bool isValid = (statusCode == 204 && string.IsNullOrEmpty(body))
                            || (statusCode == 200 && body.Contains("cloudflare"));

                if (isValid)
                {
                    return new NetworkResult(
                        NetworkStatus.Online,
                        3,
                        sw.ElapsedMilliseconds,
                        $"HTTP {statusCode}");
                }
            }
            catch
            {
                // Failed — try next endpoint
                continue;
            }
        }

        return null;
    }
}
