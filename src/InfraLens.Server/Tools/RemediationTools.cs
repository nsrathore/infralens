using System.ComponentModel;
using InfraLens.Server.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InfraLens.Server.Tools;

/// <summary>
/// Write tier. Not registered unless InfraLens:MaxTier = Write, and even then every call needs an approval token
/// minted outside the model's reach (weeks 9-12, see docs/DESIGN.md "Approvals"). Stubbed until then so the
/// tier plumbing and its tests exist from day one.
/// </summary>
[McpServerToolType]
public sealed class RemediationTools
{
    [McpServerTool(Name = "ecs_force_new_deployment", Title = "Restart service tasks",
        ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [ToolTier(ToolTier.Write)]
    [Description("Replaces every task in a service with a fresh one. Requires an approval token from a human.")]
    public static string ForceNewDeployment(
        [Description("Service name")] string service,
        [Description("Approval token issued by `infralens approve`")] string approvalToken) =>
        throw new McpException("Write actions are not implemented yet (planned for weeks 9-12).");
}
