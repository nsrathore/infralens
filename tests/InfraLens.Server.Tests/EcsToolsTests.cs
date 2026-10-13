using Amazon.ECS;
using Amazon.ECS.Model;
using InfraLens.Server.Safety;
using InfraLens.Server.Tools;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using NSubstitute;
using Xunit;

namespace InfraLens.Server.Tests;

public class EcsToolsTests
{
    private readonly IAmazonECS _ecs = Substitute.For<IAmazonECS>();
    private readonly EcsTools _tools;

    public EcsToolsTests() => _tools = new EcsTools(_ecs, new ScopeGuard(Options.Create(new InfraLensOptions())));

    [Fact]
    public async Task Describe_service_maps_counts_deployments_and_events()
    {
        _ecs.DescribeServicesAsync(Arg.Any<DescribeServicesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DescribeServicesResponse
            {
                Services =
                [
                    new Service
                    {
                        ServiceName = "infralens-api",
                        Status = "ACTIVE",
                        DesiredCount = 2,
                        RunningCount = 0,
                        PendingCount = 2,
                        TaskDefinition = "arn:aws:ecs:us-east-2:1:task-definition/infralens-api:8",
                        Deployments =
                        [
                            new Deployment
                            {
                                Id = "ecs-svc/1", Status = "PRIMARY", RolloutState = DeploymentRolloutState.IN_PROGRESS,
                                TaskDefinition = "arn:aws:ecs:us-east-2:1:task-definition/infralens-api:8",
                                DesiredCount = 2, RunningCount = 0, FailedTasks = 5,
                            },
                        ],
                        Events = [new ServiceEvent { Message = "(service infralens-api) has started 2 tasks" }],
                    },
                ],
            });

        var health = await _tools.DescribeService("infralens-api");

        Assert.Equal(0, health.Running);
        Assert.Equal("infralens-api:8", health.TaskDefinition);
        var deployment = Assert.Single(health.Deployments);
        Assert.Equal("IN_PROGRESS", deployment.RolloutState);
        Assert.Equal(5, deployment.FailedTasks);
        Assert.Single(health.RecentEvents);
    }

    [Fact]
    public async Task Out_of_scope_service_never_reaches_aws()
    {
        await Assert.ThrowsAsync<McpException>(() => _tools.DescribeService("payments"));
        await _ecs.DidNotReceiveWithAnyArgs().DescribeServicesAsync(default!, default);
    }

    [Fact]
    public async Task No_stopped_tasks_returns_empty()
    {
        _ecs.ListTasksAsync(Arg.Any<ListTasksRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ListTasksResponse { TaskArns = [] });

        Assert.Empty(await _tools.ListStoppedTasks("infralens-api"));
    }
}
