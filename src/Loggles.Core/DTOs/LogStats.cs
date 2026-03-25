namespace Loggles.Core.DTOs;

public record LogStats
{
    public int TotalCount { get; init; }
    public Dictionary<string, int> ByLevel { get; init; } = [];
    public Dictionary<string, int> BySource { get; init; } = [];
}
