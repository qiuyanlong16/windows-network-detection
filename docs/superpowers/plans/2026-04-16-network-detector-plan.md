# Network Detector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone C# .NET 8 console application that detects internet connectivity using a 3-layer fallback strategy (NCSI → UDP DNS → TCP connect).

**Architecture:** Three independent probe classes cascade sequentially. Each probe returns a `NetworkResult`. The orchestrator in `Program.cs` tries each layer and returns on first success. No third-party dependencies — pure .NET BCL + P/Invoke COM.

**Tech Stack:** C#, .NET 8, Windows COM (P/Invoke), `System.Net.Sockets`

---

## File Structure

All files under `NetworkDetector/`:

| File | Responsibility |
|------|---------------|
| `NetworkDetector.csproj` | Project file: .NET 8 console app, win-x64 target |
| `NetworkResult.cs` | Shared result model: `NetworkStatus` enum, `NetworkResult` record |
| `NcsiProbe.cs` | Layer 1: P/Invoke `INetworkListManager.IsConnectedToInternet` |
| `DnsProbe.cs` | Layer 2: Manual DNS query over UDP, validates response |
| `TcpProbe.cs` | Layer 3: `TcpClient.ConnectAsync` with timeout |
| `Program.cs` | Entry point, arg parsing (`--json`), orchestrates probes |

---

### Task 1: Project scaffold and shared types

**Files:**
- Create: `NetworkDetector/NetworkDetector.csproj`
- Create: `NetworkDetector/NetworkResult.cs`

- [ ] **Step 1: Create the project file**

On Windows, in the repo root:

```bash
dotnet new console -n NetworkDetector --framework net8.0
```

Then update `NetworkDetector/NetworkDetector.csproj` to:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the shared result model**

Create `NetworkDetector/NetworkResult.cs`:

```csharp
namespace NetworkDetector;

public enum NetworkStatus
{
    Online,
    Offline
}

public record NetworkResult(
    NetworkStatus Status,
    int? Layer,
    long? LatencyMs,
    string? Detail = null
);
```

This defines the shared return type all probes use. `Layer` indicates which probe succeeded (1=NCSI, 2=DNS, 3=TCP). `Detail` carries extra info like which DNS server responded.

- [ ] **Step 3: Verify the project builds**

```bash
cd NetworkDetector
dotnet build
```

Expected: Build succeeds with no errors.

- [ ] **Step 4: Commit**

```bash
git add NetworkDetector/
git commit -m "feat: scaffold NetworkDetector project with shared result types"
```

---

### Task 2: Layer 1 — NCSI Probe

**Files:**
- Create: `NetworkDetector/NcsiProbe.cs`

- [ ] **Step 1: Write the NCSI probe**

Create `NetworkDetector/NcsiProbe.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkDetector;

public static class NcsiProbe
{
    // CLSID_NetworkListManager
    private static readonly Guid CLSID_NetworkListManager = new("DCB00C01-570F-4A9B-8D66-6B594D1F768E");

    public static NetworkResult? TryDetect()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var nlm = Activator.CreateInstance(
                Type.GetTypeFromCLSID(CLSID_NetworkListManager)) as INetworkListManager;

            if (nlm == null)
                return null;

            bool connected = nlm.IsConnectedToInternet;
            sw.Stop();

            Marshal.ReleaseComObject(nlm);

            return connected
                ? new NetworkResult(NetworkStatus.Online, 1, sw.ElapsedMilliseconds)
                : null;
        }
        catch
        {
            // COM not available or failed — fall through to next layer
            return null;
        }
    }

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D66-6B594D1F768E")]
    private interface INetworkListManager
    {
        // Methods 0-7 are other INetworkListManager methods we don't use.
        // We only need IsConnectedToInternet which is at index 8 in the vtable.
        // But since we're using ComImport, we must declare them in order.
        // We'll use a minimal interface with just what we need.
        // Actually, ComImport requires all methods in vtable order.
        // The full interface has 19 methods, but we can declare only up to the one we need.

        void GetNetworks(int flags);         // [out] IEnumNetworks
        void GetNetwork(int gdNetworkId);    // [in]  [out] INetwork
        void GetNetworkConnections();         // [out] IEnumNetworkConnections
        void GetNetworkConnection(int gdNetworkConnectionId); // [in] [out] INetworkConnection
        void IsConnectedToInternet();         // placeholder — we use the property below

        [DispId(5)]
        bool IsConnectedToInternet { get; }
    }
}
```

**Wait** — the COM interface vtable ordering is tricky. Let me use the correct approach. The `INetworkListManager` interface has methods in this order, and we need the `IsConnectedToInternet` property. The correct minimal declaration uses `[ComImport]` with `[DispId]`:

Actually, for COM properties accessed via `IDispatch`, we can use `dynamic` or late binding. But the cleanest approach for this specific API is:

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NetworkDetector;

public static class NcsiProbe
{
    private static readonly Guid CLSID_NetworkListManager = new("DCB00C01-570F-4A9B-8D66-6B594D1F768E");

    public static NetworkResult? TryDetect()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var nlmType = Type.GetTypeFromCLSID(CLSID_NetworkListManager);
            if (nlmType == null) return null;

            dynamic nlm = Activator.CreateInstance(nlmType)!;
            bool connected = nlm.IsConnectedToInternet;
            sw.Stop();

            return connected
                ? new NetworkResult(NetworkStatus.Online, 1, sw.ElapsedMilliseconds)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
```

This uses `dynamic` late binding, which works with COM `IDispatch`. No vtable ordering issues.

**Important:** To enable `dynamic`, the project file needs a reference to `Microsoft.CSharp`. Update `NetworkDetector.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Microsoft.CSharp" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add NetworkDetector/
git commit -m "feat: add NCSI probe (Layer 1) using COM dynamic binding"
```

---

### Task 3: Layer 2 — UDP DNS Probe

**Files:**
- Create: `NetworkDetector/DnsProbe.cs`

- [ ] **Step 1: Write the DNS probe**

Create `NetworkDetector/DnsProbe.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace NetworkDetector;

public static class DnsProbe
{
    private static readonly IPAddress[] DnsServers =
    [
        IPAddress.Parse("223.5.5.5"),       // Alibaba DNS
        IPAddress.Parse("119.29.29.29"),    // Tencent DNS
        IPAddress.Parse("114.114.114.114"), // 114 DNS
    ];

    private const int TimeoutMs = 2000;
    private const string QueryDomain = "www.baidu.com";

    public static NetworkResult? TryDetect()
    {
        var query = BuildDnsQuery(QueryDomain);

        foreach (var dnsServer in DnsServers)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var udp = new UdpClient();
                udp.Client.ReceiveTimeout = TimeoutMs;

                var endPoint = new IPEndPoint(dnsServer, 53);
                udp.Send(query, query.Length, endPoint);

                // Receive with timeout
                var receiveTask = udp.ReceiveAsync();
                if (receiveTask.Wait(TimeSpan.FromMilliseconds(TimeoutMs)))
                {
                    var response = receiveTask.Result;
                    sw.Stop();

                    if (IsValidDnsResponse(query, response.Buffer))
                    {
                        return new NetworkResult(
                            NetworkStatus.Online,
                            2,
                            sw.ElapsedMilliseconds,
                            dnsServer.ToString());
                    }
                }
            }
            catch
            {
                // Timeout or error — try next DNS server
                continue;
            }
        }

        return null;
    }

    private static byte[] BuildDnsQuery(string domain)
    {
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms);

        // Transaction ID (random)
        var txId = (ushort)new Random().Next(0, 0xFFFF);
        bw.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)txId)));

        // Flags: standard query, recursion desired (0x0100)
        bw.Write((byte)0x01);
        bw.Write((byte)0x00);

        // Questions: 1
        bw.Write((ushort)0);
        bw.Write((byte)0x01);

        // Answers, Authority, Additional: all 0
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((ushort)0);

        // Query name: length-prefixed labels
        foreach (var label in domain.Split('.'))
        {
            bw.Write((byte)label.Length);
            foreach (var c in label)
                bw.Write((byte)c);
        }
        bw.Write((byte)0); // terminating zero

        // Query type: A (1)
        bw.Write((ushort)0);
        bw.Write((byte)0x01);

        // Query class: IN (1)
        bw.Write((ushort)0);
        bw.Write((byte)0x01);

        return ms.ToArray();
    }

    private static bool IsValidDnsResponse(byte[] query, byte[] response)
    {
        if (response.Length < 12) return false;

        // Check transaction ID matches
        if (query[0] != response[0] || query[1] != response[1])
            return false;

        // Check flags: standard response (0x8180)
        // Flags are at bytes 2-3
        if (response[2] != 0x81 || response[3] != 0x80)
            return false;

        return true;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add NetworkDetector/DnsProbe.cs
git commit -m "feat: add DNS probe (Layer 2) with manual UDP DNS query"
```

---

### Task 4: Layer 3 — TCP Probe

**Files:**
- Create: `NetworkDetector/TcpProbe.cs`

- [ ] **Step 1: Write the TCP probe**

Create `NetworkDetector/TcpProbe.cs`:

```csharp
using System;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace NetworkDetector;

public static class TcpProbe
{
    private static readonly (IPAddress Ip, int Port)[] Targets =
    [
        (IPAddress.Parse("110.242.68.66"), 80),  // Baidu HTTP
        (IPAddress.Parse("223.5.5.5"), 53),      // Alibaba DNS (TCP)
    ];

    private const int TimeoutMs = 3000;

    public static NetworkResult? TryDetect()
    {
        foreach (var (ip, port) in Targets)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var tcp = new TcpClient();

                var connectTask = tcp.ConnectAsync(ip, port);
                if (connectTask.Wait(TimeSpan.FromMilliseconds(TimeoutMs)))
                {
                    sw.Stop();
                    tcp.Close();

                    return new NetworkResult(
                        NetworkStatus.Online,
                        3,
                        sw.ElapsedMilliseconds,
                        $"{ip}:{port}");
                }
                else
                {
                    // Timeout
                    tcp.Close();
                }
            }
            catch
            {
                // Connection failed — try next target
                continue;
            }
        }

        return null;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add NetworkDetector/TcpProbe.cs
git commit -m "feat: add TCP probe (Layer 3) with connect-only test"
```

---

### Task 5: Program entry point and orchestrator

**Files:**
- Modify: `NetworkDetector/Program.cs`

- [ ] **Step 1: Write Program.cs**

Replace the auto-generated `NetworkDetector/Program.cs` with:

```csharp
using System;
using System.Text.Json;
using NetworkDetector;

var useJson = args.Contains("--json");

NetworkResult? result = null;

// Layer 1: NCSI
result ??= NcsiProbe.TryDetect();

// Layer 2: DNS
result ??= DnsProbe.TryDetect();

// Layer 3: TCP
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
```

- [ ] **Step 2: Verify build**

```bash
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 3: Test on Windows (online)**

```bash
dotnet run
```

Expected output: `Online`
Exit code: `0`

```bash
dotnet run -- --json
```

Expected output (example):
```json
{"status":"online","layer":1,"latency_ms":0,"detail":null}
```

- [ ] **Step 4: Commit**

```bash
git add NetworkDetector/Program.cs
git commit -m "feat: add Program.cs with layer-cascade orchestrator and --json output"
```

---

### Task 6: Build and publish standalone exe

**Files:**
- No file changes — this is a build/publish step.

- [ ] **Step 1: Publish self-contained single-file exe**

```bash
cd NetworkDetector
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ../bin
```

Expected: `bin/NetworkDetector.exe` is created (~50-70 MB self-contained).

- [ ] **Step 2: Test the published exe**

```bash
../bin/NetworkDetector.exe
```

Expected: `Online`

```bash
../bin/NetworkDetector.exe --json
```

Expected: JSON output with status.

- [ ] **Step 3: Commit**

```bash
git add bin/.gitignore
git commit -m "build: publish self-contained win-x64 single-file exe"
```

Create `bin/.gitignore` first if needed:

```
*
!.gitignore
```

---

## Verification Summary

After all tasks, verify:

```bash
# Build
cd NetworkDetector
dotnet build

# Run with default output
dotnet run
# Expected: Online, exit code 0

# Run with JSON output
dotnet run -- --json
# Expected: {"status":"online","layer":1,...}

# Publish standalone exe
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ../bin
../bin/NetworkDetector.exe
```
