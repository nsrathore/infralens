variable "region" {
  type    = string
  default = "us-east-2"
}

variable "alert_email" {
  type        = string
  description = "Where budget alerts go."
}

variable "monthly_budget_usd" {
  type    = number
  default = 20
}
