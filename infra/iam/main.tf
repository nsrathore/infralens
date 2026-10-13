# The real permission boundary for InfraLens. One role per tool tier; the MCP server runs under
# an AWS profile that assumes exactly one of them. Persistent (not torn down with the sandbox).

terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  backend "s3" {
    key          = "iam/terraform.tfstate"
    region       = "us-east-2"
    use_lockfile = true
  }
}

provider "aws" {
  region = var.region
  default_tags {
    tags = { project = "infralens", stack = "iam" }
  }
}

data "aws_caller_identity" "current" {}

locals {
  account = data.aws_caller_identity.current.account_id
  arn_pfx = "arn:aws"
}

data "aws_iam_policy_document" "trust" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "AWS"
      identifiers = [var.trusted_principal_arn]
    }
  }
}

# ---------------------------------------------------------------- audit table
resource "aws_dynamodb_table" "audit" {
  name         = "infralens-audit"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "session_id"
  range_key    = "sk"

  attribute {
    name = "session_id"
    type = "S"
  }

  attribute {
    name = "sk"
    type = "S"
  }

  ttl {
    attribute_name = "expires_at"
    enabled        = true
  }
}

# ---------------------------------------------------------------- reader (tiers: read + query)
data "aws_iam_policy_document" "reader" {
  statement {
    sid = "DescribeCompute"
    actions = [
      "ecs:Describe*",
      "ecs:List*",
      "elasticloadbalancing:Describe*",
      "application-autoscaling:Describe*",
    ]
    resources = ["*"]
  }

  statement {
    sid = "QueryLogsInScope"
    actions = [
      "logs:StartQuery",
      "logs:FilterLogEvents",
      "logs:GetLogEvents",
    ]
    resources = ["${local.arn_pfx}:logs:${var.region}:${local.account}:log-group:/ecs/infralens-sandbox*"]
  }

  statement {
    sid       = "QueryResults"
    actions   = ["logs:GetQueryResults", "logs:StopQuery", "logs:DescribeLogGroups", "logs:DescribeQueries"]
    resources = ["*"]
  }

  statement {
    sid = "Metrics"
    actions = [
      "cloudwatch:GetMetricData",
      "cloudwatch:DescribeAlarms",
      "cloudwatch:DescribeAlarmHistory",
      "cloudwatch:ListMetrics",
    ]
    resources = ["*"]
  }

  statement {
    sid = "QueuesAndTables"
    actions = [
      "sqs:GetQueueAttributes",
      "sqs:ListQueues",
      "sqs:ListDeadLetterSourceQueues",
      "dynamodb:DescribeTable",
      "dynamodb:ListTables",
    ]
    resources = ["*"]
  }

  statement {
    sid = "IamIntrospection"
    actions = [
      "iam:GetRole",
      "iam:GetRolePolicy",
      "iam:ListRolePolicies",
      "iam:ListAttachedRolePolicies",
      "iam:SimulatePrincipalPolicy",
      "secretsmanager:DescribeSecret",
    ]
    resources = ["*"]
  }

  statement {
    sid       = "WriteOwnAudit"
    actions   = ["dynamodb:PutItem"]
    resources = [aws_dynamodb_table.audit.arn]
  }

  # Belt and braces: no secret values, no data plane reads, no state changes — even if someone
  # later attaches a broader managed policy to this role.
  statement {
    sid    = "NeverReadSecretsOrData"
    effect = "Deny"
    actions = [
      "secretsmanager:GetSecretValue",
      "ssm:GetParameter*",
      "kms:Decrypt",
      "s3:GetObject",
      "dynamodb:GetItem",
      "dynamodb:Query",
      "dynamodb:Scan",
      "sqs:ReceiveMessage",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role" "reader" {
  name                 = "infralens-reader"
  assume_role_policy   = data.aws_iam_policy_document.trust.json
  max_session_duration = 3600
}

resource "aws_iam_role_policy" "reader" {
  name   = "infralens-reader"
  role   = aws_iam_role.reader.id
  policy = data.aws_iam_policy_document.reader.json
}

# ---------------------------------------------------------------- operator (tier: write), weeks 9–12
data "aws_iam_policy_document" "operator_writes" {
  statement {
    sid       = "DeployAndScaleSandboxOnly"
    actions   = ["ecs:UpdateService"]
    resources = ["${local.arn_pfx}:ecs:${var.region}:${local.account}:service/infralens-sandbox/infralens-api"]
  }

  statement {
    sid       = "RedriveSandboxDlqOnly"
    actions   = ["sqs:StartMessageMoveTask", "sqs:ListMessageMoveTasks"]
    resources = ["${local.arn_pfx}:sqs:${var.region}:${local.account}:infralens-orders-dlq"]
  }
}

resource "aws_iam_role" "operator" {
  name                 = "infralens-operator"
  assume_role_policy   = data.aws_iam_policy_document.trust.json
  max_session_duration = 3600
}

resource "aws_iam_role_policy" "operator_reads" {
  name   = "infralens-reader"
  role   = aws_iam_role.operator.id
  policy = data.aws_iam_policy_document.reader.json
}

resource "aws_iam_role_policy" "operator_writes" {
  name   = "infralens-operator-writes"
  role   = aws_iam_role.operator.id
  policy = data.aws_iam_policy_document.operator_writes.json
}
