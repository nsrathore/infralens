variable "region" {
  type    = string
  default = "us-east-2"
}

variable "trusted_principal_arn" {
  type        = string
  description = "Your IAM user or SSO role ARN. Only it may assume the InfraLens roles."
}
