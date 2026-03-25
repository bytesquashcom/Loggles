namespace Loggles.Core.DTOs;

public record ErrorSpike
{
    public DateTime BucketStart { get; init; }
    public int ErrorCount { get; init; }
    public int TotalCount { get; init; }
}
