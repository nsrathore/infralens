using InfraLens.Server.Audit;
using InfraLens.Server.Safety;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Xunit;

namespace InfraLens.Server.Tests;

public class ToolCallGuardTests
{
    private sealed class MemoryAudit : IAuditSink
    {
        public List<AuditRecord> Records { get; } = [];

        public ValueTask WriteAsync(AuditRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }

    private readonly MemoryAudit _audit = new();

    private ToolCallGuard Guard(InfraLensOptions? o = null)
    {
        var options = Options.Create(o ?? new InfraLensOptions());
        var registry = new ToolRegistry(new Dictionary<string, ToolTier>
        {
            ["read_tool"] = ToolTier.Read,
            ["write_tool"] = ToolTier.Write,
        });
        return new ToolCallGuard(registry, new CallBudget(options), _audit, options, TimeProvider.System);
    }

    private static Func<CancellationToken, ValueTask<CallToolResult>> Returns(string text) =>
        _ => ValueTask.FromResult(new CallToolResult { Content = [new TextContentBlock { Text = text }] });

    private static string TextOf(CallToolResult r) => ((TextContentBlock)r.Content[0]).Text;

    [Fact]
    public async Task Read_tool_runs_and_is_audited()
    {
        var result = await Guard().InvokeAsync("read_tool", null, "s1", Returns("fine"), CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var record = Assert.Single(_audit.Records);
        Assert.Equal("ok", record.Outcome);
        Assert.Equal("Read", record.Tier);
    }

    [Fact]
    public async Task Write_tool_is_refused_on_a_read_tier_server()
    {
        var called = false;
        var result = await Guard().InvokeAsync("write_tool", null, "s1", _ =>
        {
            called = true;
            return ValueTask.FromResult(new CallToolResult());
        }, CancellationToken.None);

        Assert.False(called);
        Assert.True(result.IsError);
        Assert.Equal("refused_tier", Assert.Single(_audit.Records).Outcome);
    }

    [Fact]
    public async Task Budget_stops_a_looping_agent()
    {
        var guard = Guard(new InfraLensOptions { MaxCallsPerSession = 2 });
        await guard.InvokeAsync("read_tool", null, "s1", Returns("1"), CancellationToken.None);
        await guard.InvokeAsync("read_tool", null, "s1", Returns("2"), CancellationToken.None);
        var third = await guard.InvokeAsync("read_tool", null, "s1", Returns("3"), CancellationToken.None);

        Assert.True(third.IsError);
        Assert.Equal("refused_budget", _audit.Records[^1].Outcome);
    }

    [Fact]
    public async Task Output_is_redacted_and_capped()
    {
        var guard = Guard(new InfraLensOptions { MaxOutputChars = 200 });
        var noisy = "password=hunter2\n" + string.Concat(Enumerable.Repeat("x\n", 500));

        var result = await guard.InvokeAsync("read_tool", null, "s1", Returns(noisy), CancellationToken.None);

        Assert.DoesNotContain("hunter2", TextOf(result));
        Assert.Contains("[truncated:", TextOf(result));
    }

    [Fact]
    public async Task Unknown_tool_is_refused() =>
        Assert.True((await Guard().InvokeAsync("nope", null, null, Returns(""), CancellationToken.None)).IsError);
}
