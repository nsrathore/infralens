using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InfraLens.Server;

/// <summary>OpenTelemetry sources. Every tool call is one span plus a duration histogram sample.</summary>
public static class Telemetry
{
    public const string Name = "InfraLens";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);

    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("infralens.tool.duration", unit: "ms", description: "Tool call latency");

    public static readonly Counter<long> ToolCalls =
        Meter.CreateCounter<long>("infralens.tool.calls", description: "Tool calls by tool and outcome");
}
