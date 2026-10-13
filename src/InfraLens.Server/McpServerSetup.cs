using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatchLogs;
using Amazon.DynamoDBv2;
using Amazon.ECS;
using InfraLens.Server.Audit;
using InfraLens.Server.Safety;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace InfraLens.Server;

public static class McpServerSetup
{
    /// <summary>Options, AWS clients, safety services, audit and telemetry. Shared by stdio and HTTP hosts.</summary>
    public static InfraLensOptions AddInfraLensCore(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(InfraLensOptions.Section);
        services.AddOptions<InfraLensOptions>().Bind(section);
        var options = section.Get<InfraLensOptions>() ?? new InfraLensOptions();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ScopeGuard>();
        services.AddSingleton<CallBudget>();
        services.AddSingleton(ToolRegistry.FromAssembly(typeof(McpServerSetup).Assembly));
        services.AddSingleton<ToolCallGuard>();

        // AWS: credentials come from the default chain (AWS_PROFILE locally, OIDC role in CI).
        // Point AWS_PROFILE at the infralens-readonly role profile, never at an admin profile.
        var aws = config.GetAWSOptions();
        aws.Region = RegionEndpoint.GetBySystemName(options.Region);
        services.AddDefaultAWSOptions(aws);
        services.AddAWSService<IAmazonCloudWatchLogs>();
        services.AddAWSService<IAmazonCloudWatch>();
        services.AddAWSService<IAmazonECS>();

        services.AddSingleton<StderrAuditSink>();
        if (string.IsNullOrWhiteSpace(options.AuditTableName))
        {
            services.AddSingleton<IAuditSink>(sp => sp.GetRequiredService<StderrAuditSink>());
        }
        else
        {
            services.AddAWSService<IAmazonDynamoDB>();
            services.AddSingleton<IAuditSink, DynamoDbAuditSink>();
        }

        // Traces + metrics for every tool call. Exported only when an OTLP endpoint is configured
        // (local: an OTel collector / ADOT sidecar forwarding to CloudWatch; see docs/DESIGN.md).
        var otel = services.AddOpenTelemetry()
            .WithTracing(t => t.AddSource(Telemetry.Name, "Experimental.ModelContextProtocol"))
            .WithMetrics(m => m.AddMeter(Telemetry.Name));
        if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return options;
    }

    /// <summary>Registers only the tool classes allowed at <paramref name="maxTier"/> and wraps every call in the guard.</summary>
    public static IMcpServerBuilder WithInfraLensTools(this IMcpServerBuilder builder, ToolTier maxTier)
    {
        builder.WithTools(ToolRegistry.ToolTypesUpTo(typeof(McpServerSetup).Assembly, maxTier));

        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
        {
            var services = request.Services ?? request.Server.Services
                ?? throw new InvalidOperationException("No service provider on the request.");
            var guard = services.GetRequiredService<ToolCallGuard>();
            return await guard.InvokeAsync(
                request.Params!.Name,
                request.Params.Arguments,
                request.Server.SessionId,
                ct => next(request, ct),
                cancellationToken);
        }));

        return builder;
    }
}
