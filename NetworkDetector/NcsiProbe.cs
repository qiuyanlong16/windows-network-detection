using System;
using System.Diagnostics;

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
            // COM not available or failed — fall through to next layer
            return null;
        }
    }
}
