using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Sandbox.Api;

/// <summary>Drains the orders queue: marks each order PROCESSED after WORK_SECONDS of "work".</summary>
public sealed class OrderWorker(
    IAmazonSQS sqs,
    IAmazonDynamoDB dynamo,
    SandboxSettings settings,
    Faults faults,
    ILogger<OrderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Worker polling {QueueUrl}", settings.WorkerQueueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            ReceiveMessageResponse batch;
            try
            {
                batch = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = settings.WorkerQueueUrl,
                    MaxNumberOfMessages = 5,
                    WaitTimeSeconds = 20,
                    MessageSystemAttributeNames = ["ApproximateReceiveCount"],
                }, stoppingToken);
            }
            catch (AmazonSQSException ex)
            {
                log.LogError(ex, "ReceiveMessage failed for {QueueUrl}", settings.WorkerQueueUrl);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            foreach (var message in batch.Messages ?? [])
            {
                await HandleAsync(message, stoppingToken);
            }
        }
    }

    private async Task HandleAsync(Message message, CancellationToken ct)
    {
        var receiveCount = message.Attributes?.GetValueOrDefault("ApproximateReceiveCount") ?? "1";
        try
        {
            var order = JsonSerializer.Deserialize<OrderMessage>(message.Body)
                ?? throw new JsonException("Empty order message");
            if (string.IsNullOrEmpty(order.OrderId))
            {
                throw new JsonException("Order message has no orderId");
            }

            if (faults.RejectOrders)
            {
                throw new InvalidOperationException($"Order {order.OrderId} failed validation: unsupported currency 'null'");
            }

            await Task.Delay(TimeSpan.FromSeconds(settings.WorkSeconds), ct);

            await dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = settings.OrdersTable,
                Key = new Dictionary<string, AttributeValue> { ["order_id"] = new() { S = order.OrderId } },
                UpdateExpression = "SET #s = :p",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":p"] = new() { S = "PROCESSED" } },
            }, ct);

            await sqs.DeleteMessageAsync(settings.WorkerQueueUrl, message.ReceiptHandle, ct);
            log.LogInformation("Processed order {OrderId} (receive count {ReceiveCount})", order.OrderId, receiveCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not deleted: the message becomes visible again after the visibility timeout and is retried,
            // until maxReceiveCount moves it to the DLQ (if one is configured).
            log.LogError(ex, "Failed to process message {MessageId} (receive count {ReceiveCount})", message.MessageId, receiveCount);
        }
    }
}
