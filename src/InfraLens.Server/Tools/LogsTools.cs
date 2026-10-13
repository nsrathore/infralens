using System.ComponentModel;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using InfraLens.Server.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InfraLens.Server.Tools;

public sealed record LogGroupInfo(string Name, long StoredBytes, int? RetentionDays);

public sealed record LogQueryResult(
    string LogGroup,
    string Query,
    DateTimeOffset From,
    DateTimeOffset To,
    int RowCount,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

[McpServerToolType]
public sealed class LogsTools(IAmazonCloudWatchLogs logs, ScopeGuard scope, TimeProvider time)
{
    private const int MaxRows = 200;
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    [McpServerTool(Name = "logs_list_groups", Title = "List log groups",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [ToolTier(ToolTier.Read)]
    [Description("Lists the CloudWatch log groups InfraLens may read. Start here to find a service's log group name.")]
    public async Task<IReadOnlyList<LogGroupInfo>> ListGroups(CancellationToken cancellationToken)
    {
        var groups = new List<LogGroupInfo>();
        foreach (var prefix in scope.LogGroupPrefixes)
        {
            var response = await logs.DescribeLogGroupsAsync(
                new DescribeLogGroupsRequest { LogGroupNamePrefix = prefix, Limit = 50 }, cancellationToken);
            groups.AddRange((response.LogGroups ?? []).Select(g =>
                new LogGroupInfo(g.LogGroupName, g.StoredBytes ?? 0, g.RetentionInDays)));
        }
        return groups;
    }

    [McpServerTool(Name = "logs_query", Title = "Query logs (Logs Insights)",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [ToolTier(ToolTier.Read)]
    [Description("""
        Runs a CloudWatch Logs Insights query against one log group and returns the rows.
        Example query: fields @timestamp, level, message | filter level = "Error" | sort @timestamp desc
        Keep windows short and add filters; output is capped.
        """)]
    public async Task<LogQueryResult> Query(
        [Description("Full log group name, e.g. /ecs/infralens-sandbox/api")] string logGroup,
        [Description("Logs Insights query string. A '| limit N' is appended if missing.")] string query,
        [Description("How far back to search, in minutes (1-1440).")] int minutesBack = 30,
        [Description("Maximum rows to return (1-200).")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        scope.LogGroup(logGroup);
        var window = scope.Window(minutesBack);
        limit = Math.Clamp(limit, 1, MaxRows);
        if (!query.Contains("| limit", StringComparison.OrdinalIgnoreCase))
        {
            query = $"{query} | limit {limit}";
        }

        var to = time.GetUtcNow();
        var from = to - window;
        var started = await logs.StartQueryAsync(new StartQueryRequest
        {
            LogGroupNames = [logGroup],
            QueryString = query,
            StartTime = from.ToUnixTimeSeconds(),
            EndTime = to.ToUnixTimeSeconds(),
            Limit = limit,
        }, cancellationToken);

        var deadline = time.GetUtcNow() + QueryTimeout;
        while (true)
        {
            var response = await logs.GetQueryResultsAsync(
                new GetQueryResultsRequest { QueryId = started.QueryId }, cancellationToken);

            if (response.Status == QueryStatus.Complete)
            {
                var rows = (response.Results ?? [])
                    .Select(fields => (IReadOnlyDictionary<string, string>)(fields ?? [])
                        .Where(f => f.Field is not null && f.Field != "@ptr")
                        .GroupBy(f => f.Field!)
                        .ToDictionary(g => g.Key, g => g.First().Value ?? ""))
                    .ToList();
                return new LogQueryResult(logGroup, query, from, to, rows.Count, rows);
            }

            if (response.Status == QueryStatus.Failed || response.Status == QueryStatus.Cancelled ||
                response.Status == QueryStatus.Timeout)
            {
                throw new McpException($"Logs Insights query ended with status {response.Status?.Value}. Check the query syntax.");
            }

            if (time.GetUtcNow() > deadline)
            {
                await logs.StopQueryAsync(new StopQueryRequest { QueryId = started.QueryId }, cancellationToken);
                throw new McpException($"Query did not finish within {QueryTimeout.TotalSeconds}s. Narrow the window or add a filter.");
            }

            await Task.Delay(PollInterval, time, cancellationToken);
        }
    }

    [McpServerTool(Name = "logs_recent_errors", Title = "Recent errors and warnings",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [ToolTier(ToolTier.Read)]
    [Description("Shortcut: the most frequent error and warning messages in a log group, with counts and last-seen time. Good first look; follow up with logs_query.")]
    public Task<LogQueryResult> RecentErrors(
        [Description("How far back to search, in minutes (1-1440).")] int minutesBack = 30,
        [Description("Log group; defaults to the API's log group.")] string? logGroup = null,
        CancellationToken cancellationToken = default)
    {
        const string query = """
            fields @timestamp, coalesce(message, @message) as msg
            | filter @message like /(?i)(error|exception|fatal|warn|timeout|throttl|denied|oom|killed)/
            | stats count(*) as hits, latest(@timestamp) as last_seen by msg
            | sort hits desc
            """;
        return Query(logGroup ?? scope.DefaultLogGroup, query, minutesBack, 25, cancellationToken);
    }
}
