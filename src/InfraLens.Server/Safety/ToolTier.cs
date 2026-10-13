namespace InfraLens.Server.Safety;

/// <summary>Permission tiers. Each tier maps to its own IAM role in infra/sandbox/iam_infralens.tf.</summary>
public enum ToolTier
{
    /// <summary>Describe/list/query only. Safe to run unattended.</summary>
    Read = 0,

    /// <summary>Reads that can expose sensitive material (task env vars, full request payloads). Redacted, opt-in.</summary>
    Sensitive = 1,

    /// <summary>Changes the environment (restart, scale, roll back). Needs a human approval token. Weeks 9-12.</summary>
    Write = 2,
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolTierAttribute(ToolTier tier) : Attribute
{
    public ToolTier Tier { get; } = tier;
}
