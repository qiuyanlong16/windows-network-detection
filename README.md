# Windows Network Detector

A lightweight C# .NET 8 console application that detects internet connectivity on Windows machines using a 4-layer fallback strategy — designed to avoid false positives from proxies and false negatives from HTTP-level blocking.

**Requirements:** Windows 10 or later, .NET 8 runtime.

## Problem

Traditional network checks fail in two opposite directions:

- **False negatives**: HTTP-based checks (e.g. `HttpClient.GetAsync("https://www.google.com")`) fail even when the network works. HTTP depends on DNS → TCP → TLS → HTTP, and failure at any layer produces a false "no network" result.
- **False positives**: System proxies (e.g. TUN-based VPN/代理) create virtual network adapters that stay "up" even when the real network is down. TCP connections succeed because they connect to the local proxy process, not the internet.

## Solution

Four independent probes cascade sequentially. Each layer validates at a different level, from fastest/lightest to most thorough.

| Layer | Method | Protocol | What it verifies | Timeout |
|-------|--------|----------|-----------------|---------|
| 1 | Windows NCSI | COM API (zero I/O) | OS-level connectivity state | — |
| 2 | DNS Query | UDP → public DNS | DNS resolution works | 2s/server |
| 3 | HTTP GET | HTTP with `UseProxy=false` | Real internet data (bypasses proxy) | 2s/endpoint |
| 4 | TCP + HTTP validate | TCP SYN + raw HTTP GET | Connection returns real HTTP response | 4s/target |

Worst-case total: ~12 seconds (all layers fail).

## Usage

### Default output

```powershell
.\NetworkDetector.exe
```

Output:
```
Online
```

Exit code `0` = Online, `1` = Offline.

### JSON output

```powershell
.\NetworkDetector.exe --json
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
├── HttpProbe.cs         # Layer 3: HTTP GET bypassing proxy, verifies response body
└── TcpProbe.cs          # Layer 4: TCP connect + HTTP validate (catches proxy-faked connections)
```

## Design Decisions

- **Proxy bypass** — Layer 3 sets `UseProxy = false` and verifies the actual response body (not just status code) to guard against proxies returning fake responses. Layer 4 sends a raw HTTP GET over a raw TCP socket, completely bypassing the .NET HTTP stack and any system proxy configuration.
- **China-accessible targets** — DNS probes use Alibaba (223.5.5.5), Tencent (119.29.29.29), and 114 (114.114.114.114). HTTP probes use `connectivitycheck.gstatic.com` and `cp.cloudflare.com`. TCP targets are Baidu (110.242.68.66) and Alibaba DNS (223.5.5.5).
- **No third-party dependencies** — pure .NET BCL + P/Invoke for Windows COM.
- **No retries within a layer** — if a probe fails, move to the next target or layer. This keeps the worst-case latency bounded.

## Integration

This is distributed as a standalone exe for testing. To integrate into your WPF/WPF+WebView2 project, reference the `NetworkDetector` project directly and call:

```csharp
var result = NcsiProbe.TryDetect()
    ?? DnsProbe.TryDetect()
    ?? HttpProbe.TryDetect()
    ?? TcpProbe.TryDetect()
    ?? new NetworkResult(NetworkStatus.Offline, null, null);

bool isOnline = result.Status == NetworkStatus.Online;
```
