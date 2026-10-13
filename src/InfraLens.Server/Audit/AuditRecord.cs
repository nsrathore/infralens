using System.Text.Json;
using System.Text.Json.Serialization;

namespace InfraLens.Server.Audit;

/// <summary>One row per tool call, allowed or refused. Arguments are stored redacted.</summary>
public sealed record AuditRecord(
    string CallId,
    DateTimeOffset At,
    string? SessionId,
    string Tool,
    string Tier,
    string Outcome,          // ok | tool_error | refused_tier | refused_budget | unknown_tool | exception
    long DurationMs,
    int OutputChars,
    string ArgumentsJson,
    string? Detail,
    string? TraceId);

public interface IAuditSink
{
    ValueTask WriteAsync(AuditRecord record, CancellationToken cancellationToken);
}

/// <summary>Default sink: one JSON line per call on stderr (stdout belongs to the MCP stdio transport).</summary>
public sealed class StderrAuditSink : IAuditSink
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async ValueTask WriteAsync(AuditRecord record, CancellationToken cancellationToken) =>
        await Console.Error.WriteLineAsync("audit " + JsonSerializer.Serialize(record, Json).AsMemory(), cancellationToken);
}
