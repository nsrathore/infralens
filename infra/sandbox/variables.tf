# Defaults describe a HEALTHY sandbox. Each scenario in incidents/scenarios.yaml overrides a few of
# these through a JSON var file (eval/infralens_eval/inject.py). eval/tests checks that every key a
# scenario sets is declared here.

variable "name" {
  type    = string
  default = "infralens-sandbox"
}

variable "region" {
  type    = string
  default = "us-east-2"
}

variable "image_tag" {
  type    = string
  default = "latest"
}

variable "cpu_architecture" {
  type        = string
  default     = "X86_64"
  description = "X86_64 or ARM64; must match the image platform built by scripts/push-image.sh."
}

# ---------- service shape ----------
variable "desired_count" {
  type    = number
  default = 1
}

variable "task_cpu" {
  type    = number
  default = 512
}

variable "task_memory" {
  type    = number
  default = 1024
}

variable "autoscaling_max" {
  type    = number
  default = 4
}

variable "health_check_grace_period" {
  type    = number
  default = 60
}

variable "circuit_breaker" {
  type    = bool
  default = false
}

# ---------- app configuration ----------
variable "fault_mode" {
  type        = string
  default     = "none"
  description = "Comma-separated app fault switches: leak, reject_orders, slow_db, slow_start, cpu_heavy, noisy_logs, injected_log."
}

variable "app_listen_port" {
  type        = number
  default     = 8080
  description = "PORT env var the app listens on. The container port mapping and target group stay on 8080."
}

variable "work_seconds" {
  type    = number
  default = 2
}

variable "omit_env" {
  type        = list(string)
  default     = []
  description = "Environment variable names to leave out of the task definition."
}

variable "api_key_secret_arn_override" {
  type        = string
  default     = ""
  description = "If set, API_KEY is read from this secret ARN instead of the sandbox's own secret."
}

variable "use_legacy_queue" {
  type    = bool
  default = false
}

# ---------- load balancer / network ----------
variable "health_check_path" {
  type    = string
  default = "/health"
}

variable "alb_idle_timeout" {
  type    = number
  default = 60
}

variable "allow_alb_to_app" {
  type    = bool
  default = true
}

variable "allowed_ingress_cidr" {
  type        = string
  default     = "0.0.0.0/0"
  description = "Who may reach the ALB on port 80. Narrow to your IP/32 when convenient."
}

# ---------- IAM (task role) ----------
variable "task_role_dynamodb_write" {
  type    = bool
  default = true
}

variable "task_role_sqs_send" {
  type    = bool
  default = true
}

# ---------- data ----------
variable "dynamodb_max_write_request_units" {
  type        = number
  default     = null
  description = "On-demand write throughput cap (WRU/s). null = uncapped."
}

variable "create_customer_gsi" {
  type    = bool
  default = true
}

variable "queue_visibility_timeout" {
  type    = number
  default = 60
}

variable "max_receive_count" {
  type    = number
  default = 5
}

variable "enable_dlq" {
  type    = bool
  default = true
}
