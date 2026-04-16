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
