# Windows Network Detector

A lightweight C# .NET 8 console application that detects internet connectivity on Windows machines using a 3-layer fallback strategy — designed to avoid HTTP-level blocking that causes false negatives.

**Requirements:** Windows 10 or later, .NET 8 runtime.

## Problem

Traditional HTTP-based network checks (e.g. `HttpClient.GetAsync("https://www.google.com")`) fail even when the network is available. HTTP depends on a long chain: DNS → TCP → TLS → HTTP. Failure at any layer produces a false "no network" result. In some environments, certain public endpoints are throttled or blocked while the network itself works fine.

## Solution

Three independent probes cascade sequentially. Each layer is progressively heavier but more resilient. If any layer confirms connectivity, the check returns "Online" immediately.

| Layer | Method | Protocol | Timeout | Latency |
|-------|--------|----------|---------|---------|
| 1 | Windows NCSI | COM API (zero network I/O) | — | < 1ms |
| 2 | DNS Query | UDP → 223.5.5.5 / 119.29.29.29 / 114.114.114.114 | 2s per server | ~50ms |
| 3 | TCP Connect | TCP SYN → baidu.com:80 / alidns:53 | 3s per target | ~100ms |

Worst-case total: ~12 seconds (all layers fail).

## Usage

### Default output

```bash
NetworkDetector.exe
```

Output:
```
Online
```

Exit code `0` = Online, `1` = Offline.

### JSON output

```bash
NetworkDetector.exe --json
```

Output:
```json
{"status":"online","layer":1,"latency_ms":0,"detail":null}
```

## Build

```bash
cd NetworkDetector
dotnet build
dotnet run
```

### Publish standalone executable

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ../bin
```

This produces a single `NetworkDetector.exe` (~50-70 MB) with the .NET runtime bundled — no runtime installation needed on the target machine.

## Project Structure

```
NetworkDetector/
├── Program.cs           # Entry point, arg parsing, layer orchestrator
├── NetworkResult.cs     # Shared result model (enum + record)
├── NcsiProbe.cs         # Layer 1: Windows NCSI COM query
├── DnsProbe.cs          # Layer 2: Manual UDP DNS query (no third-party lib)
└── TcpProbe.cs          # Layer 3: TCP connect test (SYN only, no data)
```

## Design Decisions

- **No HTTP anywhere** — Layer 2 uses raw UDP DNS packets (manually constructed, no library). Layer 3 only completes a TCP handshake and immediately closes the connection.
- **China-accessible DNS targets** — Alibaba (223.5.5.5), Tencent (119.29.29.29), and 114 (114.114.114.114) are used as Layer 2 probes.
- **No third-party dependencies** — pure .NET BCL + P/Invoke for Windows COM.
- **No retries within a layer** — if a probe fails, move to the next DNS server or next layer. This keeps the worst-case latency bounded.

## Integration

This is distributed as a standalone exe for testing. To integrate into your WPF/WPF+WebView2 project, reference the `NetworkDetector` project directly and call:

```csharp
var result = NcsiProbe.TryDetect()
    ?? DnsProbe.TryDetect()
    ?? TcpProbe.TryDetect()
    ?? new NetworkResult(NetworkStatus.Offline, null, null);

bool isOnline = result.Status == NetworkStatus.Online;
```
