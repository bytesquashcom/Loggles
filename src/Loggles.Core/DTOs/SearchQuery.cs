using Loggles.Core.Models;

namespace Loggles.Core.DTOs;

public record SearchQuery
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public LogLevel? LevelMin { get; init; }
    public string? Source { get; init; }
    public string? LoggerName { get; init; }
    public string? CorrelationId { get; init; }
    public string? Text { get; init; }
    public string? MessageTemplate { get; init; }
    public Dictionary<string, string>? Properties { get; init; }
    public int PageSize { get; init; } = 100;
    public string? PageToken { get; init; }
}
