using InfraLens.Server.Safety;
using InfraLens.Server.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Xunit;

namespace InfraLens.Server.Tests;

public class ToolRegistryTests
{
    private static readonly System.Reflection.Assembly ServerAssembly = typeof(McpServerSetup).Assembly;

    [Fact]
    public void Every_tool_has_a_name_and_tier()
    {
        var registry = ToolRegistry.FromAssembly(ServerAssembly); // throws if any tool is untiered or unnamed
        Assert.Contains("logs_query", registry.Names);
        Assert.Equal(ToolTier.Read, registry.TierOf("ecs_service_health"));
        Assert.Equal(ToolTier.Write, registry.TierOf("ecs_force_new_deployment"));
    }

    [Fact]
    public void Read_tier_server_does_not_register_write_tools()
    {
        var types = ToolRegistry.ToolTypesUpTo(ServerAssembly, ToolTier.Read).ToList();
        Assert.Contains(typeof(LogsTools), types);
        Assert.Contains(typeof(EcsTools), types);
        Assert.DoesNotContain(typeof(RemediationTools), types);
    }

    [Fact]
    public void Write_tier_server_registers_everything()
    {
        var types = ToolRegistry.ToolTypesUpTo(ServerAssembly, ToolTier.Write).ToList();
        Assert.Contains(typeof(RemediationTools), types);
    }
}

public class RedactorTests
{
    [Theory]
    [InlineData("key AKIAABCDEFGHIJKLMNOP used", "AKIAABCDEFGHIJKLMNOP")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc.def", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("db password=hunter2; retrying", "hunter2")]
    [InlineData("{\"apiKey\": \"abc123\"}", "abc123")]
    [InlineData("ANTHROPIC sk-ant-api03-abcdefghijklmnop", "sk-ant-api03")]
    public void Secrets_are_masked(string input, string secret)
    {
        var output = Redactor.Redact(input);
        Assert.DoesNotContain(secret, output);
        Assert.Contains(Redactor.Mask, output);
    }

    [Fact]
    public void Ordinary_text_is_untouched()
    {
        const string line = "GET /orders/42 returned 503 after 2004 ms";
        Assert.Equal(line, Redactor.Redact(line));
    }
}

public class OutputBudgetTests
{
    [Fact]
    public void Short_text_passes_through() => Assert.Equal("abc", OutputBudget.Truncate("abc", 10));

    [Fact]
    public void Long_text_is_cut_on_a_line_and_labelled()
    {
        var text = string.Join('\n', Enumerable.Range(0, 1000).Select(i => $"line {i}"));
        var cut = OutputBudget.Truncate(text, 500);
        Assert.True(cut.Length < 700);
        Assert.Contains("[truncated:", cut);
    }
}

public class ScopeGuardTests
{
    private readonly ScopeGuard _scope = new(Options.Create(new InfraLensOptions()));

    [Fact]
    public void Allowed_log_group_passes() => _scope.LogGroup("/ecs/infralens-sandbox/api");

    [Theory]
    [InlineData("/aws/lambda/payroll")]
    [InlineData("/ecs/infralens-sandboxx/api")]
    [InlineData("")]
    public void Other_log_groups_are_refused(string group) =>
        Assert.Throws<McpException>(() => _scope.LogGroup(group));

    [Fact]
    public void Unknown_service_is_refused() => Assert.Throws<McpException>(() => _scope.Service("billing"));

    [Theory]
    [InlineData("infralens-api:7")]
    [InlineData("arn:aws:ecs:us-east-2:123456789012:task-definition/infralens-api:7")]
    public void Own_task_definitions_pass(string td) => _scope.TaskDefinition(td);

    [Fact]
    public void Foreign_task_definition_is_refused() =>
        Assert.Throws<McpException>(() => _scope.TaskDefinition("prod-payments:3"));

    [Fact]
    public void Window_is_clamped() => Assert.Equal(TimeSpan.FromMinutes(1440), _scope.Window(99_999));
}
