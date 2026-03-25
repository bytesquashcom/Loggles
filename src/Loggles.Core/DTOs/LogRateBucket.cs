namespace Loggles.Core.DTOs;

public record LogRateBucket
{
    public DateTime BucketStart { get; init; }
    public int Count { get; init; }
}
