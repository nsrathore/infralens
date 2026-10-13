resource "aws_dynamodb_table" "orders" {
  name         = "infralens-orders"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "order_id"

  attribute {
    name = "order_id"
    type = "S"
  }

  attribute {
    name = "customer_id"
    type = "S"
  }

  # Incident missing-gsi drops this index; GET /customers/{id}/orders depends on it.
  dynamic "global_secondary_index" {
    for_each = var.create_customer_gsi ? [1] : []
    content {
      name            = "by_customer"
      hash_key        = "customer_id"
      projection_type = "ALL"
    }
  }

  # Incident dynamodb-throttling caps on-demand write throughput.
  dynamic "on_demand_throughput" {
    for_each = var.dynamodb_max_write_request_units == null ? [] : [1]
    content {
      max_write_request_units = var.dynamodb_max_write_request_units
    }
  }
}

resource "aws_sqs_queue" "orders_dlq" {
  name                      = "infralens-orders-dlq"
  message_retention_seconds = 86400
}

resource "aws_sqs_queue" "orders" {
  name                       = "infralens-orders"
  visibility_timeout_seconds = var.queue_visibility_timeout
  receive_wait_time_seconds  = 20
  redrive_policy = var.enable_dlq ? jsonencode({
    deadLetterTargetArn = aws_sqs_queue.orders_dlq.arn
    maxReceiveCount     = var.max_receive_count
  }) : null
}

# Nothing consumes this queue. Incident wrong-queue-url points the API at it.
resource "aws_sqs_queue" "orders_legacy" {
  name                      = "infralens-orders-legacy"
  message_retention_seconds = 86400
}

resource "random_password" "api_key" {
  length  = 32
  special = false
}

resource "aws_secretsmanager_secret" "api_key" {
  name                    = "infralens/api-key"
  recovery_window_in_days = 0 # allow destroy + recreate every session
}

resource "aws_secretsmanager_secret_version" "api_key" {
  secret_id     = aws_secretsmanager_secret.api_key.id
  secret_string = random_password.api_key.result
}

resource "aws_cloudwatch_log_group" "api" {
  name              = "/ecs/${var.name}/api"
  retention_in_days = 3
}
