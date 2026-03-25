namespace Loggles.Core.Models;

public record LogEvent
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Source { get; init; } = string.Empty;
    public string? LoggerName { get; init; }
    public string? CorrelationId { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? MessageTemplate { get; init; }
    public string? Exception { get; init; }
    public string? PropertiesJson { get; init; }
}
