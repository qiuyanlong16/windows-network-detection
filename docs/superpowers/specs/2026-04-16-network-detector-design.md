# Network Detection Design

**Date**: 2026-04-16
**Status**: Draft

## Problem

A WPF + WebView2 application needs to detect internet connectivity on Windows machines at cold start. The current approach (C# HTTP request to a public endpoint) fails even when the network is available — likely being blocked/throttled. HTTP is too high-level: it depends on DNS → TCP → TLS → HTTP, and failure at any layer produces a false negative.

## Requirements

- Detect whether the machine can reach the internet (binary: online/offline)
- Works reliably in mainland China network environments
- Delivered as a standalone C# console exe for testing, later migratable into WPF project as a class
- No third-party dependencies — pure .NET BCL + P/Invoke
- Fast: total detection should complete within ~15 seconds in the worst case

## Architecture

### Overview

```
┌─────────────────────────────────────────┐
│           NetworkDetector.exe            │
│                                          │
│  ┌────────────────────────────────────┐  │
│  │        DetectNetwork()             │  │
│  │                                    │  │
│  │  Layer 1: NCSI (0ms)               │  │
│  │    └→ true → return Online         │  │
│  │    └→ false → Layer 2              │  │
│  │                                    │  │
│  │  Layer 2: UDP DNS Query (<2s)      │  │
│  │    └→ response → return Online     │  │
│  │    └→ timeout → Layer 3            │  │
│  │                                    │  │
│  │  Layer 3: TCP Connect (<3s)        │  │
│  │    └→ connected → return Online    │  │
│  │    └→ timeout → return Offline     │  │
│  └────────────────────────────────────┘  │
│                                          │
│  Exit code: 0 = Online, 1 = Offline      │
│  --json flag: output JSON details         │
└─────────────────────────────────────────┘
```

### Fallback Strategy

Three layers cascade sequentially. Each layer is a progressively heavier probe. If any layer confirms connectivity, the function returns `Online` immediately. Only when all three layers fail does it return `Offline`.

This approach ensures that:
- Layer 1 catches the common case instantly (no network IO)
- Layer 2 catches cases where Windows NCSI is unreliable but DNS is reachable (DNS port 53 is rarely blocked)
- Layer 3 catches edge cases where DNS is blocked but raw TCP can still reach known IPs

### Tech Stack

- **Language**: C# (.NET 8)
- **Target**: Windows x64
- **Publish**: Single-file, self-contained (`dotnet publish -r win-x64 --self-contained`)
- **Dependencies**: None (pure .NET Base Class Library + P/Invoke for COM)

## Layer 1: NCSI (Windows Network Connectivity Status Indicator)

### What

Query Windows' own internet connectivity judgment via COM interface.

### How

P/Invoke `NetworkListManager` COM object (`CLSID_NetworkListManager`) → call `INetworkListManager.IsConnectedToInternet` → return `bool`.

### Targets

- COM CLSID: `{DCB00C01-570F-4A9B-8D66-6B594D1F768E}`
- Interface: `INetworkListManager`

### Exit criteria

- `IsConnectedToInternet == true` → Online (latency < 1ms)
- `IsConnectedToInternet == false` → proceed to Layer 2

### Rationale

The taskbar network icon uses this exact API. It's the fastest possible check with zero network IO. However, it reads cached state maintained by Windows' own NCSI probe, which can be unreliable in some network environments, so it's only the first layer.

## Layer 2: UDP DNS Query

### What

Manually construct and send a DNS query over UDP to public DNS servers. Receiving a valid DNS response proves internet connectivity.

### How

1. Construct a standard DNS Query message (~40 bytes) using `MemoryStream`:
   - Transaction ID: random 2 bytes
   - Flags: `0x0100` (standard query, recursion desired)
   - Questions: 1, Answers: 0, Authority: 0, Additional: 0
   - Query name: `www.baidu.com` (encoded as length-prefixed labels)
   - Query type: `A` (0x0001), Query class: `IN` (0x0001)

2. Send to target DNS server on port 53 via `UdpClient.SendAsync`

3. Wait for response with `UdpClient.ReceiveAsync` (timeout: 2 seconds per server)

4. Validate response:
   - First 2 bytes match transaction ID
   - Flags byte pair equals `0x81` `0x80` (standard response, no error)
   - If valid → Online

### DNS Targets (Mainland China accessible)

| # | Server | Provider |
|---|--------|----------|
| 1 | `223.5.5.5` | Alibaba DNS |
| 2 | `119.29.29.29` | Tencent DNS |
| 3 | `114.114.114.114` | 114 DNS |

Servers are tried sequentially. Each has a 2-second timeout. Any response → Online. All failures → Layer 3.

### Rationale

DNS over UDP port 53 is one of the most resilient protocols on the internet. Blocking it effectively means blocking all internet access. It's much lighter than HTTP — a single UDP datagram, no handshake, no TLS, no state.

## Layer 3: TCP Connection Test

### What

Attempt a TCP three-way handshake to known server IPs. If the connection is established, internet connectivity is confirmed. No application-layer data is sent.

### How

Use `TcpClient.ConnectAsync` with timeout:

```csharp
using var tcp = new TcpClient();
var task = tcp.ConnectAsync(ipAddress, port);
var connected = await task.WaitAsync(timeout);
tcp.Close(); // immediately close, no data sent
```

### Targets

| # | Target IP | Port | Description |
|---|-----------|------|-------------|
| 1 | `110.242.68.66` | 80 | Baidu HTTP |
| 2 | `223.5.5.5` | 53 | Alibaba DNS (also listens on TCP 53) |

IPs are hardcoded (no runtime DNS resolution needed). Each target has a 3-second timeout. Any successful connection → Online. All failures → Offline.

### Rationale

TCP SYN is the most basic internet connectivity test short of ICMP. It doesn't depend on DNS working, doesn't require TLS negotiation, and doesn't send any application data that could trigger content filters.

## Output

### Default (plain text)

```
Online
```
or
```
Offline
```

### `--json` flag

```json
{"status": "online", "layer": 1, "latency_ms": 0}
{"status": "online", "layer": 2, "latency_ms": 42, "dns_server": "223.5.5.5"}
{"status": "offline", "layer": null, "latency_ms": null}
```

### Exit codes

- `0` → Online
- `1` → Offline

## Error Handling

- Each layer has its own timeout; if exceeded, move to the next layer
- Total worst-case timeout: Layer 2 (3 × 2s) + Layer 3 (2 × 3s) = ~12 seconds
- Exceptions in any layer are caught and treated as "layer failed", proceeding to the next
- No retries within a layer — if a probe fails, move on

## File Structure

```
NetworkDetector/
├── Program.cs            # Entry point, arg parsing, exit code
├── NetworkDetector.csproj
├── NcsiProbe.cs          # Layer 1: COM NCSI query
├── DnsProbe.cs           # Layer 2: UDP DNS query
├── TcpProbe.cs           # Layer 3: TCP connection test
└── NetworkResult.cs      # Result model (status, layer, latency)
```
