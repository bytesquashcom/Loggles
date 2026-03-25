using Loggles.Core.Models;

namespace Loggles.Core.DTOs;

public record SearchResult
{
    public IReadOnlyList<LogEvent> Items { get; init; } = [];
    public string? NextPageToken { get; init; }
}
