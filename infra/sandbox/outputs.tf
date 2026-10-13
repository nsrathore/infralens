# Names here are a contract with eval/infralens_eval/inject.py and the scripts.

output "alb_url" {
  value = "http://${aws_lb.main.dns_name}"
}

output "cluster" {
  value = aws_ecs_cluster.main.name
}

output "service" {
  value = aws_ecs_service.api.name
}

output "listener_arn" {
  value = aws_lb_listener.http.arn
}

output "target_group_arn" {
  value = aws_lb_target_group.api.arn
}

output "orders_queue_url" {
  value = aws_sqs_queue.orders.url
}

output "orders_dlq_url" {
  value = aws_sqs_queue.orders_dlq.url
}

output "legacy_queue_url" {
  value = aws_sqs_queue.orders_legacy.url
}

output "log_group" {
  value = aws_cloudwatch_log_group.api.name
}
