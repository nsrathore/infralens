using InfraLens.Server.Safety;

namespace InfraLens.Server;

/// <summary>Everything the server is allowed to touch, and how much. Bound from the "InfraLens" config section.</summary>
public sealed class InfraLensOptions
{
    public const string Section = "InfraLens";

    public string Region { get; set; } = "us-east-2";

    /// <summary>The only ECS cluster tools may read.</summary>
    public string ClusterName { get; set; } = "infralens-sandbox";

    /// <summary>ECS services inside <see cref="ClusterName"/> that tools may read.</summary>
    public string[] AllowedServices { get; set; } = ["infralens-api"];

    /// <summary>Task definition families tools may read.</summary>
    public string TaskFamilyPrefix { get; set; } = "infralens-";

    /// <summary>Log groups must start with one of these prefixes.</summary>
    public string[] AllowedLogGroupPrefixes { get; set; } = ["/ecs/infralens-sandbox/"];

    /// <summary>Log group used when a tool is called without one.</summary>
    public string DefaultLogGroup { get; set; } = "/ecs/infralens-sandbox/api";

    /// <summary>Highest tier the server will execute. Write tools are not even registered below Write.</summary>
    public ToolTier MaxTier { get; set; } = ToolTier.Read;

    /// <summary>Hard cap on characters returned by one tool call (protects the model's context window).</summary>
    public int MaxOutputChars { get; set; } = 12_000;

    /// <summary>Tool calls allowed per server process / session before the server refuses.</summary>
    public int MaxCallsPerSession { get; set; } = 60;

    /// <summary>Widest time window a log or metric query may cover.</summary>
    public int MaxWindowMinutes { get; set; } = 24 * 60;

    /// <summary>DynamoDB table for audit records. Null = audit to stderr only (local dev).</summary>
    public string? AuditTableName { get; set; }
}
