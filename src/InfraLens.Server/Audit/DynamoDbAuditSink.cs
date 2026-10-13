using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace InfraLens.Server.Audit;

/// <summary>
/// Writes audit rows to the infralens-audit table (infra/iam): session_id + sk = timestamp#callId, 30-day TTL.
/// Never throws into the tool pipeline: an audit failure is logged and the stderr copy still exists.
/// </summary>
public sealed class DynamoDbAuditSink(
    IAmazonDynamoDB dynamo,
    IOptions<InfraLensOptions> options,
    StderrAuditSink fallback,
    ILogger<DynamoDbAuditSink> logger) : IAuditSink
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    public async ValueTask WriteAsync(AuditRecord r, CancellationToken cancellationToken)
    {
        await fallback.WriteAsync(r, cancellationToken);

        var item = new Dictionary<string, AttributeValue>
        {
            ["session_id"] = new() { S = r.SessionId ?? "local" },
            ["sk"] = new() { S = $"{r.At:O}#{r.CallId}" },
            ["tool"] = new() { S = r.Tool },
            ["tier"] = new() { S = r.Tier },
            ["outcome"] = new() { S = r.Outcome },
            ["duration_ms"] = new() { N = r.DurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["output_chars"] = new() { N = r.OutputChars.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["arguments"] = new() { S = r.ArgumentsJson },
            ["expires_at"] = new() { N = r.At.Add(Retention).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        if (r.Detail is not null) item["detail"] = new() { S = r.Detail };
        if (r.TraceId is not null) item["trace_id"] = new() { S = r.TraceId };

        try
        {
            await dynamo.PutItemAsync(new PutItemRequest { TableName = options.Value.AuditTableName, Item = item }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Audit write to DynamoDB failed for call {CallId}", r.CallId);
        }
    }
}
