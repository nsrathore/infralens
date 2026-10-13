using System.ComponentModel;
using Amazon.ECS;
using Amazon.ECS.Model;
using InfraLens.Server.Safety;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace InfraLens.Server.Tools;

public sealed record DeploymentInfo(
    string? Id, string? Status, string? RolloutState, string? RolloutStateReason, string? TaskDefinition,
    int Desired, int Running, int Pending, int FailedTasks, DateTime? CreatedAt, DateTime? UpdatedAt);

public sealed record ServiceEventInfo(DateTime? At, string? Message);

public sealed record ServiceHealth(
    string Service, string? Status, int Desired, int Running, int Pending, string? TaskDefinition,
    bool CircuitBreakerEnabled, bool CircuitBreakerRollback,
    IReadOnlyList<DeploymentInfo> Deployments, IReadOnlyList<ServiceEventInfo> RecentEvents);

public sealed record ContainerExit(string? Name, int? ExitCode, string? Reason, string? LastStatus);

public sealed record StoppedTask(
    string? TaskId, string? TaskDefinition, string? StopCode, string? StoppedReason, DateTime? StartedAt,
    DateTime? StoppedAt, string? CapacityProvider, IReadOnlyList<ContainerExit> Containers);

public sealed record ContainerSpec(
    string? Name, string? Image, int? Cpu, int? MemoryHardMiB, int? MemorySoftMiB,
    IReadOnlyList<string> EnvVarNames, IReadOnlyList<string> SecretNames, IReadOnlyList<string>? HealthCheck,
    IReadOnlyList<int> ContainerPorts);

public sealed record TaskDefinitionSummary(
    string? Arn, string? Family, int? Revision, string? Cpu, string? Memory, string? TaskRoleArn,
    IReadOnlyList<ContainerSpec> Containers);

[McpServerToolType]
public sealed class EcsTools(IAmazonECS ecs, ScopeGuard scope)
{
    [McpServerTool(Name = "ecs_service_health", Title = "ECS service health",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [ToolTier(ToolTier.Read)]
    [Description("Desired/running/pending counts, active and recent deployments with rollout state, and the latest service events for one ECS service. Use first when a service looks unhealthy.")]
    public async Task<ServiceHealth> DescribeService(
        [Description("Service name, e.g. infralens-api")] string service,
        [Description("How many recent service events to include (1-25).")] int events = 10,
        CancellationToken cancellationToken = default)
    {
        scope.Service(service);
        var response = await ecs.DescribeServicesAsync(
            new DescribeServicesRequest { Cluster = scope.Cluster, Services = [service] }, cancellationToken);

        var svc = (response.Services ?? []).FirstOrDefault()
            ?? throw new McpException($"Service '{service}' not found in cluster '{scope.Cluster}'.");

        var breaker = svc.DeploymentConfiguration?.DeploymentCircuitBreaker;
        return new ServiceHealth(
            svc.ServiceName,
            svc.Status,
            svc.DesiredCount ?? 0,
            svc.RunningCount ?? 0,
            svc.PendingCount ?? 0,
            ShortArn(svc.TaskDefinition),
            breaker?.Enable ?? false,
            breaker?.Rollback ?? false,
            (svc.Deployments ?? []).Select(d => new DeploymentInfo(
                d.Id, d.Status, d.RolloutState?.Value, d.RolloutStateReason, ShortArn(d.TaskDefinition),
                d.DesiredCount ?? 0, d.RunningCount ?? 0, d.PendingCount ?? 0, d.FailedTasks ?? 0,
                d.CreatedAt, d.UpdatedAt)).ToList(),
            (svc.Events ?? []).Take(Math.Clamp(events, 1, 25))
                .Select(e => new ServiceEventInfo(e.CreatedAt, e.Message)).ToList());
    }

    [McpServerTool(Name = "ecs_stopped_tasks", Title = "Recently stopped tasks",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [ToolTier(ToolTier.Read)]
    [Description("Recently stopped tasks for a service with stop code, stopped reason and each container's exit code. Explains crash loops, OOM kills (exit 137), failed health checks and image pull errors. ECS keeps stopped tasks for about an hour.")]
    public async Task<IReadOnlyList<StoppedTask>> ListStoppedTasks(
        [Description("Service name, e.g. infralens-api")] string service,
        [Description("Maximum tasks to return (1-20).")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        scope.Service(service);
        var list = await ecs.ListTasksAsync(new ListTasksRequest
        {
            Cluster = scope.Cluster,
            ServiceName = service,
            DesiredStatus = DesiredStatus.STOPPED,
            MaxResults = Math.Clamp(limit, 1, 20),
        }, cancellationToken);

        var arns = list.TaskArns ?? [];
        if (arns.Count == 0)
        {
            return [];
        }

        var described = await ecs.DescribeTasksAsync(
            new DescribeTasksRequest { Cluster = scope.Cluster, Tasks = arns }, cancellationToken);

        return (described.Tasks ?? [])
            .OrderByDescending(t => t.StoppedAt)
            .Select(t => new StoppedTask(
                t.TaskArn?.Split('/').Last(),
                ShortArn(t.TaskDefinitionArn),
                t.StopCode?.Value,
                t.StoppedReason,
                t.StartedAt,
                t.StoppedAt,
                t.CapacityProviderName,
                (t.Containers ?? []).Select(c => new ContainerExit(c.Name, c.ExitCode, c.Reason, c.LastStatus)).ToList()))
            .ToList();
    }

    [McpServerTool(Name = "ecs_task_definition", Title = "Task definition summary",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [ToolTier(ToolTier.Read)]
    [Description("Image, CPU/memory, health check, ports and the NAMES of environment variables and secrets for a task definition. Values are never returned at this tier. Compare two revisions to see what a deployment changed.")]
    public async Task<TaskDefinitionSummary> DescribeTaskDefinition(
        [Description("Family, family:revision, or ARN, e.g. infralens-api:7")] string taskDefinition,
        CancellationToken cancellationToken = default)
    {
        scope.TaskDefinition(taskDefinition);
        var response = await ecs.DescribeTaskDefinitionAsync(
            new DescribeTaskDefinitionRequest { TaskDefinition = taskDefinition }, cancellationToken);
        var td = response.TaskDefinition ?? throw new McpException($"Task definition '{taskDefinition}' not found.");

        return new TaskDefinitionSummary(
            td.TaskDefinitionArn, td.Family, td.Revision, td.Cpu, td.Memory, td.TaskRoleArn,
            (td.ContainerDefinitions ?? []).Select(c => new ContainerSpec(
                c.Name, c.Image, c.Cpu, c.Memory, c.MemoryReservation,
                (c.Environment ?? []).Select(e => e.Name).OfType<string>().Order().ToList(),
                (c.Secrets ?? []).Select(s => s.Name).OfType<string>().Order().ToList(),
                c.HealthCheck?.Command,
                (c.PortMappings ?? []).Select(p => p.ContainerPort ?? 0).ToList())).ToList());
    }

    private static string? ShortArn(string? arn) => arn?.Split('/').Last();
}
