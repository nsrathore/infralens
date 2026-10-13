data "aws_iam_policy_document" "ecs_tasks_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# ---------- execution role: pull the image, write logs, resolve secrets ----------
resource "aws_iam_role" "execution" {
  name               = "${var.name}-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "execution_secrets" {
  statement {
    # Scoped to the infralens/ prefix, so a wrong ARN fails as "not found", not "access denied".
    actions   = ["secretsmanager:GetSecretValue"]
    resources = ["arn:aws:secretsmanager:${var.region}:${data.aws_caller_identity.me.account_id}:secret:infralens/*"]
  }
}

resource "aws_iam_role_policy" "execution_secrets" {
  name   = "secrets"
  role   = aws_iam_role.execution.id
  policy = data.aws_iam_policy_document.execution_secrets.json
}

# ---------- task role: what the app code may do ----------
locals {
  queue_arns = [aws_sqs_queue.orders.arn, aws_sqs_queue.orders_legacy.arn]
}

data "aws_iam_policy_document" "task" {
  statement {
    sid       = "OrdersRead"
    actions   = ["dynamodb:GetItem", "dynamodb:Query"]
    resources = [aws_dynamodb_table.orders.arn, "${aws_dynamodb_table.orders.arn}/index/*"]
  }

  # Incidents task-role-missing-dynamodb-write and log-prompt-injection drop this statement.
  dynamic "statement" {
    for_each = var.task_role_dynamodb_write ? [1] : []
    content {
      sid       = "OrdersWrite"
      actions   = ["dynamodb:PutItem", "dynamodb:UpdateItem"]
      resources = [aws_dynamodb_table.orders.arn]
    }
  }

  statement {
    sid       = "QueueConsume"
    actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:ChangeMessageVisibility", "sqs:GetQueueAttributes"]
    resources = local.queue_arns
  }

  # Incident red-herring-sqs-send-denied drops this statement.
  dynamic "statement" {
    for_each = var.task_role_sqs_send ? [1] : []
    content {
      sid       = "QueueSend"
      actions   = ["sqs:SendMessage"]
      resources = local.queue_arns
    }
  }
}

resource "aws_iam_role" "task" {
  name               = "${var.name}-task"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_assume.json
}

resource "aws_iam_role_policy" "task" {
  name   = "orders"
  role   = aws_iam_role.task.id
  policy = data.aws_iam_policy_document.task.json
}
