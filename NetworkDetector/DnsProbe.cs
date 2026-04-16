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
