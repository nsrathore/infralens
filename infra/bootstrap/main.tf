# Apply ONCE with your admin profile and keep it. Costs ~$0/month idle.
# Holds what must survive sandbox teardowns: state bucket, budget alarm, ECR repository.
# The InfraLens IAM roles and audit table live in infra/iam (also persistent).

terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
  # Local state for this tiny stack; commit nothing from .terraform/ or *.tfstate (see .gitignore).
}

provider "aws" {
  region = var.region
  default_tags {
    tags = { project = "infralens", stack = "bootstrap" }
  }
}

data "aws_caller_identity" "me" {}

locals {
  account_id = data.aws_caller_identity.me.account_id
}

# ---------- Terraform state for the sandbox stack ----------
resource "aws_s3_bucket" "state" {
  bucket = "infralens-tfstate-${local.account_id}"
}

resource "aws_s3_bucket_versioning" "state" {
  bucket = aws_s3_bucket.state.id
  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_public_access_block" "state" {
  bucket                  = aws_s3_bucket.state.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# ---------- Budget alarm ($20/month) ----------
resource "aws_budgets_budget" "monthly" {
  name         = "infralens-monthly"
  budget_type  = "COST"
  limit_amount = tostring(var.monthly_budget_usd)
  limit_unit   = "USD"
  time_unit    = "MONTHLY"

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 50
    threshold_type             = "PERCENTAGE"
    notification_type          = "ACTUAL"
    subscriber_email_addresses = [var.alert_email]
  }

  notification {
    comparison_operator        = "GREATER_THAN"
    threshold                  = 100
    threshold_type             = "PERCENTAGE"
    notification_type          = "FORECASTED"
    subscriber_email_addresses = [var.alert_email]
  }
}

# ---------- Container registry for the sandbox image ----------
resource "aws_ecr_repository" "sandbox" {
  name                 = "infralens-sandbox"
  image_tag_mutability = "MUTABLE"
  force_delete         = true
}

resource "aws_ecr_lifecycle_policy" "sandbox" {
  repository = aws_ecr_repository.sandbox.name
  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Keep the last 5 images"
      selection    = { tagStatus = "any", countType = "imageCountMoreThan", countNumber = 5 }
      action       = { type = "expire" }
    }]
  })
}
