terraform {
  required_version = ">= 1.10"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Partial config: scripts/sandbox-up.sh passes -backend-config="bucket=infralens-tfstate-<account>".
  backend "s3" {
    key          = "sandbox/terraform.tfstate"
    region       = "us-east-2"
    use_lockfile = true
  }
}

provider "aws" {
  region = var.region
  default_tags {
    tags = { project = "infralens", stack = "sandbox" }
  }
}

data "aws_caller_identity" "me" {}
data "aws_availability_zones" "available" {
  state = "available"
}
