namespace Sandbox.Api;

public sealed record SandboxSettings(
    string OrdersTable,
    string QueueUrl,
    string WorkerQueueUrl,
    string ApiKey,
    int Port,
    int WorkSeconds,
    string FaultMode)
{
    public static SandboxSettings FromEnvironment()
    {
        static string Required(string name) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException($"Required environment variable '{name}' is not set. Refusing to start.");

        var queueUrl = Required("QUEUE_URL"); // where the API sends new orders

        return new SandboxSettings(
            OrdersTable: Required("ORDERS_TABLE"),
            QueueUrl: queueUrl,
            WorkerQueueUrl: Environment.GetEnvironmentVariable("WORKER_QUEUE_URL") is { Length: > 0 } w ? w : queueUrl, // where the worker polls
            ApiKey: Required("API_KEY"), // injected from Secrets Manager by ECS
            Port: int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port) ? port : 8080,
            WorkSeconds: int.TryParse(Environment.GetEnvironmentVariable("WORK_SECONDS"), out var work) ? work : 2,
            FaultMode: Environment.GetEnvironmentVariable("FAULT_MODE") ?? "none");
    }
}
