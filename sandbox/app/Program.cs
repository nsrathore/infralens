using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Sandbox.Api;

// The system under test: an orders API that writes to DynamoDB and enqueues work on SQS,
// plus a worker that drains the queue. Faults are switched on with FAULT_MODE (comma-separated).
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = false);

var settings = SandboxSettings.FromEnvironment(); // throws with a clear message if required config is missing
var faults = new Faults(settings.FaultMode);

if (faults.SlowStart)
{
    // Simulates a heavy cold start (cache warm-up, migrations). Paired with a short health-check grace period in incident 'health-grace-too-short'.
    Console.WriteLine(JsonSerializer.Serialize(new { level = "Information", message = "Warming caches before accepting traffic", seconds = 75 }));
    await Task.Delay(TimeSpan.FromSeconds(75));
}

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(faults);
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddHostedService<OrderWorker>();
builder.Services.AddHostedService<FaultNoise>();

var app = builder.Build();
var log = app.Logger;

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/orders", async (OrderRequest request, IAmazonDynamoDB dynamo, IAmazonSQS sqs, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerId) || request.Items is not { Count: > 0 } || request.Items.Any(i => i.Quantity <= 0))
    {
        log.LogInformation("Rejected order: validation failed for customer {CustomerId}", request.CustomerId);
        return Results.BadRequest(new { error = "customerId and at least one item with quantity > 0 are required" });
    }

    var orderId = Guid.NewGuid().ToString("n");
    faults.LeakOnRequest();
    faults.MaybeBurnCpu();
    await faults.MaybeSlowDependencyAsync(ct);

    await dynamo.PutItemAsync(new PutItemRequest
    {
        TableName = settings.OrdersTable,
        Item = new Dictionary<string, AttributeValue>
        {
            ["order_id"] = new() { S = orderId },
            ["customer_id"] = new() { S = request.CustomerId },
            ["status"] = new() { S = "PENDING" },
            ["created_at"] = new() { S = DateTimeOffset.UtcNow.ToString("O") },
            ["items"] = new() { S = JsonSerializer.Serialize(request.Items) },
        },
    }, ct);

    await sqs.SendMessageAsync(new SendMessageRequest
    {
        QueueUrl = settings.QueueUrl,
        MessageBody = JsonSerializer.Serialize(new OrderMessage(orderId, request.CustomerId)),
    }, ct);

    log.LogInformation("Accepted order {OrderId} for customer {CustomerId}", orderId, request.CustomerId);
    return Results.Accepted($"/orders/{orderId}", new { orderId });
});

app.MapGet("/orders/{orderId}", async (string orderId, IAmazonDynamoDB dynamo, CancellationToken ct) =>
{
    await faults.MaybeSlowDependencyAsync(ct);
    var response = await dynamo.GetItemAsync(new GetItemRequest
    {
        TableName = settings.OrdersTable,
        Key = new Dictionary<string, AttributeValue> { ["order_id"] = new() { S = orderId } },
    }, ct);

    return response.Item is { Count: > 0 } item
        ? Results.Ok(new { orderId, status = item["status"].S, customerId = item["customer_id"].S })
        : Results.NotFound();
});

app.MapGet("/customers/{customerId}/orders", async (string customerId, IAmazonDynamoDB dynamo, CancellationToken ct) =>
{
    var response = await dynamo.QueryAsync(new QueryRequest
    {
        TableName = settings.OrdersTable,
        IndexName = "by_customer",
        KeyConditionExpression = "customer_id = :c",
        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":c"] = new() { S = customerId } },
        Limit = 50,
    }, ct);

    return Results.Ok((response.Items ?? []).Select(i => new { orderId = i["order_id"].S, status = i["status"].S }));
});

app.MapGet("/admin/stats", (HttpRequest http) =>
    http.Headers["X-Api-Key"] == settings.ApiKey
        ? Results.Ok(new { faultMode = settings.FaultMode, workSeconds = settings.WorkSeconds })
        : Results.Unauthorized());

// Unhandled exceptions (AccessDenied, throttling, missing index) surface as 500s with a logged stack trace — that's the evidence.
app.Run($"http://0.0.0.0:{settings.Port}");

public sealed record OrderItem(string Sku, int Quantity);

public sealed record OrderRequest(string CustomerId, List<OrderItem>? Items);

public sealed record OrderMessage(string OrderId, string CustomerId);
