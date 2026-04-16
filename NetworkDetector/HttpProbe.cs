using System;
using System.Net.Http;
using System.Diagnostics;

namespace NetworkDetector;

public static class HttpProbe
{
    // Windows NCSI uses this endpoint — returns 204 No Content instantly
    private const string TestUrl = "http://connectivitycheck.gstatic.com/generate_204";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static NetworkResult? TryDetect()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false, // Bypass proxy to test raw connectivity
                DefaultProxyCredentials = null,
            };
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout,
            };

            var response = client.GetAsync(TestUrl).GetAwaiter().GetResult();
            sw.Stop();

            // 204 = genuine internet reachability
            if ((int)response.StatusCode == 204)
            {
                return new NetworkResult(
                    NetworkStatus.Online,
                    3,
                    sw.ElapsedMilliseconds,
                    $"HTTP {response.StatusCode}");
            }
        }
        catch
        {
            // Failed — fall through to next layer
        }

        return null;
    }
}
