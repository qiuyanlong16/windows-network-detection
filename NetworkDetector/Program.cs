using System;
using System.Text.Json;
using NetworkDetector;

var useJson = args.Contains("--json");

NetworkResult? result = null;

// Layer 1: NCSI (fastest, zero network I/O)
result ??= NcsiProbe.TryDetect();

// Layer 2: DNS (UDP query to public DNS servers)
result ??= DnsProbe.TryDetect();

// Layer 3: HTTP (bypasses proxy, verifies real internet reachability)
result ??= HttpProbe.TryDetect();

// Layer 4: TCP connect (raw SYN, last resort)
result ??= TcpProbe.TryDetect();

// If all layers failed, mark as offline
result ??= new NetworkResult(NetworkStatus.Offline, null, null);

// Output
if (useJson)
{
    var json = JsonSerializer.Serialize(new
    {
        status = result.Status.ToString().ToLowerInvariant(),
        layer = result.Layer,
        latency_ms = result.LatencyMs,
        detail = result.Detail
    });
    Console.WriteLine(json);
}
else
{
    Console.WriteLine(result.Status == NetworkStatus.Online ? "Online" : "Offline");
}

// Exit code
Environment.Exit(result.Status == NetworkStatus.Online ? 0 : 1);
