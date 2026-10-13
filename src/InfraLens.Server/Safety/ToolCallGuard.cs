using System.Diagnostics;
using System.Text.Json;
using InfraLens.Server.Audit;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace InfraLens.Server.Safety;

/// <summary>
/// Wraps every tools/call: tier check, call budget, span + metrics, redaction, output cap, audit row.
/// Kept free of MCP plumbing so it is unit-testable; McpServerSetup adapts it into a CallTool filter.
/// </summary>
public sealed class ToolCallGuard(
    ToolRegistry registry,
    CallBudget budget,
    IAuditSink audit,
    IOptions<InfraLensOptions> options,
    TimeProvider time)
{
    private readonly InfraLensOptions _o = options.Value;

    public async ValueTask<CallToolResult> InvokeAsync(
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        string? sessionId,
        Func<CancellationToken, ValueTask<CallToolResult>> next,
        CancellationToken cancellationToken)
    {
        var callId = Guid.NewGuid().ToString("n");
        var started = time.GetTimestamp();
        var argsJson = Redactor.Redact(JsonSerializer.Serialize(arguments ?? new Dictionary<string, JsonElement>()));

        using var activity = Telemetry.Source.StartActivity($"tool {toolName}", ActivityKind.Internal);
        activity?.SetTag("infralens.tool", toolName);
        activity?.SetTag("infralens.call_id", callId);

        var tier = registry.TierOf(toolName);
        activity?.SetTag("infralens.tier", tier?.ToString() ?? "unknown");

        if (tier is null)
        {
            return await RefuseAsync("unknown_tool", $"Unknown tool '{toolName}'.");
        }
        if (tier > _o.MaxTier)
        {
            return await RefuseAsync("refused_tier",
                $"Tool '{toolName}' is tier {tier}; this server runs at max tier {_o.MaxTier}.");
        }
        if (!budget.TryConsume())
        {
            return await RefuseAsync("refused_budget",
                $"Call budget spent ({budget.Limit} calls). Summarise what you have found so far.");
        }

        CallToolResult result;
        try
        {
            result = await next(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await WriteAuditAsync("exception", 0, ex.GetType().Name + ": " + ex.Message);
            throw;
        }

        var outputChars = 0;
        foreach (var block in result.Content.OfType<TextContentBlock>())
        {
            block.Text = OutputBudget.Truncate(Redactor.Redact(block.Text), _o.MaxOutputChars);
            outputChars += block.Text.Length;
        }

        var outcome = result.IsError == true ? "tool_error" : "ok";
        if (outcome != "ok")
        {
            activity?.SetStatus(ActivityStatusCode.Error);
        }
        await WriteAuditAsync(outcome, outputChars, null);
        return result;

        async ValueTask<CallToolResult> RefuseAsync(string why, string message)
        {
            activity?.SetStatus(ActivityStatusCode.Error, why);
            await WriteAuditAsync(why, message.Length, message);
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = message }] };
        }

        async ValueTask WriteAuditAsync(string kind, int chars, string? detail)
        {
            var elapsed = time.GetElapsedTime(started);
            Telemetry.ToolDuration.Record(elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("tool", toolName), new KeyValuePair<string, object?>("outcome", kind));
            Telemetry.ToolCalls.Add(1,
                new KeyValuePair<string, object?>("tool", toolName), new KeyValuePair<string, object?>("outcome", kind));
            activity?.SetTag("infralens.outcome", kind);

            await audit.WriteAsync(new AuditRecord(
                callId, time.GetUtcNow(), sessionId, toolName, tier?.ToString() ?? "unknown", kind,
                (long)elapsed.TotalMilliseconds, chars, argsJson, detail, activity?.TraceId.ToString()), cancellationToken);
        }
    }
}
