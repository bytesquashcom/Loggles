namespace Loggles.Core.DTOs;

public record LogQualityReport
{
    public int TotalCount { get; init; }
    public QualityCounter WithoutTemplate { get; init; } = new();
    public QualityCounter WithoutProperties { get; init; } = new();
    public List<SourceQuality> BySource { get; init; } = [];
    public List<SampleLog> SampleLogsWithoutTemplate { get; init; } = [];
}

public record QualityCounter
{
    public int Count { get; init; }
    public double Pct { get; init; }
}

public record SourceQuality
{
    public string Source { get; init; } = "";
    public int Total { get; init; }
    public int WithoutTemplate { get; init; }
    public int WithoutProperties { get; init; }
}

public record SampleLog
{
    public long Id { get; init; }
    public string Source { get; init; } = "";
    public string Level { get; init; } = "";
    public string Message { get; init; } = "";
}
