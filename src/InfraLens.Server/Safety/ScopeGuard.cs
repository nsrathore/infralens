using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace InfraLens.Server.Safety;

/// <summary>
/// Second line of defence after IAM: tools validate every resource name against the configured allowlist
/// before calling AWS, so a confused model gets a clear error instead of an AccessDenied (or worse, data).
/// </summary>
public sealed class ScopeGuard(IOptions<InfraLensOptions> options)
{
    private readonly InfraLensOptions _o = options.Value;

    public string Cluster => _o.ClusterName;

    public IReadOnlyList<string> LogGroupPrefixes => _o.AllowedLogGroupPrefixes;

    public string DefaultLogGroup => _o.DefaultLogGroup;

    /// <summary>Accepts "family", "family:rev" or a full ARN whose family starts with the allowed prefix.</summary>
    public string TaskDefinition(string taskDefinition)
    {
        var family = taskDefinition.Split('/').Last().Split(':')[0];
        if (!family.StartsWith(_o.TaskFamilyPrefix, StringComparison.Ordinal))
        {
            throw new McpException(
                $"Task definition '{taskDefinition}' is out of scope. Family must start with '{_o.TaskFamilyPrefix}'.");
        }
        return taskDefinition;
    }

    public string LogGroup(string logGroup)
    {
        if (string.IsNullOrWhiteSpace(logGroup) ||
            !_o.AllowedLogGroupPrefixes.Any(p => logGroup.StartsWith(p, StringComparison.Ordinal)))
        {
            throw new McpException(
                $"Log group '{logGroup}' is out of scope. Allowed prefixes: {string.Join(", ", _o.AllowedLogGroupPrefixes)}.");
        }
        return logGroup;
    }

    public string Service(string service)
    {
        if (!_o.AllowedServices.Contains(service, StringComparer.Ordinal))
        {
            throw new McpException(
                $"Service '{service}' is out of scope. Allowed services: {string.Join(", ", _o.AllowedServices)}.");
        }
        return service;
    }

    /// <summary>Clamps a look-back window to [1, MaxWindowMinutes].</summary>
    public TimeSpan Window(int minutesBack) =>
        TimeSpan.FromMinutes(Math.Clamp(minutesBack, 1, _o.MaxWindowMinutes));
}
