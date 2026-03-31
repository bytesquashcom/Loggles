namespace Loggles.Core.Models;

public record McpCallRecord(string ToolName, DateTime CalledAt, long DurationMs, int? ResultCount, int? EstimatedTokens);
