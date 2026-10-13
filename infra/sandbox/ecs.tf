data "aws_ecr_repository" "sandbox" {
  name = "infralens-sandbox" # created once by infra/bootstrap
}

resource "aws_ecs_cluster" "main" {
  name = var.name
}

resource "aws_ecs_cluster_capacity_providers" "main" {
  cluster_name       = aws_ecs_cluster.main.name
  capacity_providers = ["FARGATE", "FARGATE_SPOT"]

  default_capacity_provider_strategy {
    capacity_provider = "FARGATE_SPOT"
    weight            = 1
  }
}

locals {
  queue_url = var.use_legacy_queue ? aws_sqs_queue.orders_legacy.url : aws_sqs_queue.orders.url

  all_env = {
    AWS_REGION       = var.region
    ORDERS_TABLE     = aws_dynamodb_table.orders.name
    QUEUE_URL        = local.queue_url          # where the API sends new orders
    WORKER_QUEUE_URL = aws_sqs_queue.orders.url # where the in-process worker polls
    PORT             = tostring(var.app_listen_port)
    WORK_SECONDS     = tostring(var.work_seconds)
    FAULT_MODE       = var.fault_mode
  }

  env = [for k in sort(keys(local.all_env)) : { name = k, value = local.all_env[k] } if !contains(var.omit_env, k)]

  api_key_secret_arn = var.api_key_secret_arn_override != "" ? var.api_key_secret_arn_override : aws_secretsmanager_secret.api_key.arn
}

resource "aws_ecs_task_definition" "api" {
  family                   = "infralens-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = var.cpu_architecture
  }

  container_definitions = jsonencode([{
    name         = "api"
    image        = "${data.aws_ecr_repository.sandbox.repository_url}:${var.image_tag}"
    essential    = true
    portMappings = [{ containerPort = 8080, protocol = "tcp" }]
    environment  = local.env
    secrets      = [{ name = "API_KEY", valueFrom = local.api_key_secret_arn }]
    logConfiguration = {
      logDriver = "awslogs"
      options = {
        awslogs-group         = aws_cloudwatch_log_group.api.name
        awslogs-region        = var.region
        awslogs-stream-prefix = "api"
      }
    }
  }])

  depends_on = [aws_secretsmanager_secret_version.api_key]
}

resource "aws_ecs_service" "api" {
  name                              = "infralens-api"
  cluster                           = aws_ecs_cluster.main.id
  task_definition                   = aws_ecs_task_definition.api.arn
  desired_count                     = var.desired_count
  health_check_grace_period_seconds = var.health_check_grace_period

  # Scenarios are SUPPOSED to leave the service unsteady; never block an apply on it.
  wait_for_steady_state = false

  capacity_provider_strategy {
    capacity_provider = "FARGATE_SPOT"
    weight            = 1
  }

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.app.id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }

  deployment_minimum_healthy_percent = 100
  deployment_maximum_percent         = 200

  deployment_circuit_breaker {
    enable   = var.circuit_breaker
    rollback = var.circuit_breaker
  }

  depends_on = [aws_lb_listener.http, aws_ecs_cluster_capacity_providers.main]
}

# ---------- autoscaling (incident autoscaling-capped sets max = 1) ----------
resource "aws_appautoscaling_target" "api" {
  service_namespace  = "ecs"
  resource_id        = "service/${aws_ecs_cluster.main.name}/${aws_ecs_service.api.name}"
  scalable_dimension = "ecs:service:DesiredCount"
  min_capacity       = 1
  max_capacity       = var.autoscaling_max
}

resource "aws_appautoscaling_policy" "api_cpu" {
  name               = "${var.name}-cpu-target"
  policy_type        = "TargetTrackingScaling"
  service_namespace  = aws_appautoscaling_target.api.service_namespace
  resource_id        = aws_appautoscaling_target.api.resource_id
  scalable_dimension = aws_appautoscaling_target.api.scalable_dimension

  target_tracking_scaling_policy_configuration {
    target_value       = 60
    scale_out_cooldown = 60
    scale_in_cooldown  = 120
    predefined_metric_specification {
      predefined_metric_type = "ECSServiceAverageCPUUtilization"
    }
  }
}
