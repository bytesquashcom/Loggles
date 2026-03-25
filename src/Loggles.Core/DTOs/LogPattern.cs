namespace Loggles.Core.DTOs;

public record LogPattern
{
    public string Pattern { get; init; } = string.Empty;
    public int Count { get; init; }
    public string SampleMessage { get; init; } = string.Empty;
    public string Level { get; init; } = string.Empty;
}
