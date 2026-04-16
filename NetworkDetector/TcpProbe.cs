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
                        4,
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
