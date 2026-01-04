# ABOUTME: Exposes key attributes from the AWS API stack after OpenTofu apply.
# ABOUTME: Helps operators discover endpoints, service names, and repository URIs without console spelunking.

output "alb_dns_name" {
  description = "Public DNS name for the Application Load Balancer."
  value       = aws_lb.todo_backend_api.dns_name
}

output "alb_arn" {
  description = "ARN of the Application Load Balancer."
  value       = aws_lb.todo_backend_api.arn
}

output "ecs_cluster_name" {
  description = "Name of the ECS cluster hosting the API."
  value       = aws_ecs_cluster.todo_backend_api.name
}

output "ecs_service_name" {
  description = "Name of the ECS service running the API tasks."
  value       = aws_ecs_service.todo_backend_api.name
}

output "ecr_repository_url" {
  description = "URI for pushing images to ECR."
  value       = aws_ecr_repository.todo_backend_api.repository_url
}

output "db_connection_parameter" {
  description = "SSM parameter storing the API database connection string."
  value       = aws_ssm_parameter.todo_backend_db_connection.name
}
