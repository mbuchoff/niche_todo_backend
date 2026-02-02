# ABOUTME: Declares tunable inputs for the AWS Todo Backend API infrastructure stack.
# ABOUTME: Captures networking, scaling, deployment, and secret parameters consumed by OpenTofu.

variable "stack_name" {
  description = "Prefix applied to AWS resources for the Todo Backend API."
  type        = string
  default     = "todo-backend"
}

variable "aws_region" {
  description = "AWS region where the infrastructure should run."
  type        = string
  default     = "us-east-1"
}

variable "aws_profile" {
  description = "AWS CLI profile used for credentials when running OpenTofu locally."
  type        = string
  default     = "niche-todo-admin"
}

variable "vpc_cidr" {
  description = "CIDR block assigned to the VPC hosting the API."
  type        = string
  default     = "10.60.0.0/16"
}

variable "existing_vpc_id" {
  description = "Optional VPC ID to reuse instead of creating a new VPC."
  type        = string
  default     = null
}

variable "public_subnet_ids" {
  description = "Optional list of subnet IDs for ECS/ALB when reusing an existing VPC."
  type        = list(string)
  default     = null
}

variable "postgres_state_path" {
  description = "Optional path to the postgres stack state file for reusing VPC/subnets."
  type        = string
  default     = null
}

variable "allowed_ingress_cidr_blocks" {
  description = "CIDR blocks permitted to access the public load balancer."
  type        = list(string)
  default     = ["0.0.0.0/0"]
}

variable "listener_port" {
  description = "Port exposed on the Application Load Balancer."
  type        = number
  default     = 80
}

variable "container_port" {
  description = "Internal port where the container listens."
  type        = number
  default     = 8080
}

variable "desired_count" {
  description = "Number of ECS tasks to keep running."
  type        = number
  default     = 1
}

variable "task_cpu" {
  description = "Fargate task CPU units (1024 = 1 vCPU)."
  type        = number
  default     = 512
}

variable "task_memory" {
  description = "Fargate task memory in MiB."
  type        = number
  default     = 1024
}

variable "health_check_path" {
  description = "HTTP path the load balancer uses for health checks."
  type        = string
  default     = "/healthz"
}

variable "health_check_grace_period_seconds" {
  description = "Grace period before the ECS service starts counting health-check failures."
  type        = number
  default     = 60
}

variable "log_retention_days" {
  description = "Retention for the application CloudWatch log group."
  type        = number
  default     = 30
}

variable "ecr_repository_name" {
  description = "Name of the ECR repository for the API image."
  type        = string
  default     = "todo-backend-api"
}

variable "container_image" {
  description = "Fully qualified container image URI deployed to ECS."
  type        = string
  default     = null
}

variable "git_sha" {
  description = "Optional Git commit SHA exposed to the API as GIT_SHA."
  type        = string
  default     = null
}

variable "database_connection_string" {
  description = "Connection string injected into the API container."
  type        = string
  sensitive   = true

  validation {
    condition     = length(trimspace(var.database_connection_string)) > 0
    error_message = "database_connection_string must be supplied via TF_VAR_database_connection_string or a tfvars file."
  }
}

variable "database_security_group_id" {
  description = "Optional security group ID for the Postgres database to allow inbound from ECS."
  type        = string
  default     = null
}

variable "database_port" {
  description = "Port the Postgres database listens on."
  type        = number
  default     = 5432
}
