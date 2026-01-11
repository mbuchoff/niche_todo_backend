# ABOUTME: Exposes the connection details emitted by the AWS Postgres stack.
# ABOUTME: Helps developers plug the RDS endpoint into local testing without digging through AWS Console.

output "db_identifier" {
  description = "Name of the provisioned RDS instance."
  value       = aws_db_instance.todo_backend_postgres.id
}

output "db_endpoint" {
  description = "DNS endpoint for connecting to Postgres."
  value       = aws_db_instance.todo_backend_postgres.address
}

output "db_port" {
  description = "Port Postgres is listening on."
  value       = aws_db_instance.todo_backend_postgres.port
}

output "db_connection_string" {
  description = "Ready-to-use connection string for psql or connection strings."
  value = format(
    "Host=%s;Port=%d;Database=%s;Username=%s;Password=%s;Ssl Mode=Require",
    aws_db_instance.todo_backend_postgres.address,
    aws_db_instance.todo_backend_postgres.port,
    var.db_name,
    var.db_username,
    var.db_password
  )
  sensitive = true
}

output "vpc_id" {
  description = "VPC hosting the Postgres instance."
  value       = aws_vpc.todo_backend.id
}

output "public_subnet_ids" {
  description = "Public subnet IDs used by the Postgres stack."
  value       = [for subnet in aws_subnet.public : subnet.id]
}

output "db_security_group_id" {
  description = "Security group ID attached to the Postgres instance."
  value       = aws_security_group.todo_backend_postgres.id
}
