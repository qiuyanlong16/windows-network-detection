using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;

namespace NetworkDetector;

public static class TcpProbe
{
    private static readonly (string Ip, int Port, string Host)[] Targets =
    [
        ("110.242.68.66", 80, "www.baidu.com"),
        ("223.5.5.5", 53, "223.5.5.5"),
    ];

    private const int ConnectTimeoutMs = 2000;
    private const int ReadTimeoutMs = 2000;

    public static NetworkResult? TryDetect()
    {
        foreach (var (ip, port, host) in Targets)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                using var tcp = new TcpClient();

                var connectTask = tcp.ConnectAsync(IPAddress.Parse(ip), port);
                if (!connectTask.Wait(TimeSpan.FromMilliseconds(ConnectTimeoutMs)))
                {
                    tcp.Close();
                    continue;
                }

                // TCP connected — now validate by sending a minimal HTTP request
                // and checking we get a real response. This catches proxy-faked
                // connections where TCP "succeeds" but there's no real internet.
                tcp.SendTimeout = ReadTimeoutMs;
                tcp.ReceiveTimeout = ReadTimeoutMs;

                var stream = tcp.GetStream();
                var request = $"GET / HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n";
                var requestBytes = Encoding.ASCII.GetBytes(request);
                stream.Write(requestBytes, 0, requestBytes.Length);

                // Read first 16 bytes of response to verify it's HTTP
                var buffer = new byte[16];
                var readTask = stream.ReadAsync(buffer, 0, 16);
                if (!readTask.Wait(TimeSpan.FromMilliseconds(ReadTimeoutMs)))
                {
                    tcp.Close();
                    continue;
                }

                var bytesRead = readTask.Result;
                tcp.Close();
                sw.Stop();

                // A real server returns "HTTP/1." — a proxy may return garbage
                if (bytesRead >= 5 && buffer[0] == (byte)'H' && buffer[1] == (byte)'T'
                    && buffer[2] == (byte)'T' && buffer[3] == (byte)'P'
                    && buffer[4] == (byte)'/')
                {
                    return new NetworkResult(
                        NetworkStatus.Online,
                        4,
                        sw.ElapsedMilliseconds,
                        $"{ip}:{port}");
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }
}
