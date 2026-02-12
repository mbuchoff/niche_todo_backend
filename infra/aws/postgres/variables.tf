# ABOUTME: Declares all tunable inputs for the AWS Postgres stack managed by OpenTofu.
# ABOUTME: Captures connectivity, credential, scaling, and lifecycle knobs for reproducible spins.

variable "stack_name" {
  description = "Prefix used for tagging and naming AWS resources."
  type        = string
  default     = "todo-backend"
}

variable "aws_region" {
  description = "AWS region where the database infrastructure should be provisioned."
  type        = string
  default     = "us-east-1"
}

variable "aws_profile" {
  description = "Optional AWS shared credentials profile name (from ~/.aws/credentials). Leave unset for CI/CD (use env credentials / OIDC)."
  type        = string
  default     = null
}

variable "vpc_cidr" {
  description = "CIDR block for the dedicated VPC hosting the Postgres instance."
  type        = string
  default     = "10.42.0.0/16"
}

variable "allowed_cidr_blocks" {
  description = "CIDR blocks allowed to connect to Postgres over TCP 5432."
  type        = list(string)
  default     = []
}

variable "db_name" {
  description = "Logical Postgres database name."
  type        = string
  default     = "tododb"
}

variable "db_username" {
  description = "Admin username for the Postgres instance."
  type        = string
  default     = "todoadmin"
}

variable "db_password" {
  description = "Admin password for the Postgres instance."
  type        = string
  sensitive   = true
}

variable "db_instance_class" {
  description = "Instance size for the RDS Postgres instance."
  type        = string
  default     = "db.t4g.micro"
}

variable "db_allocated_storage_gb" {
  description = "Initial allocated storage in GB."
  type        = number
  default     = 20
}

variable "db_max_allocated_storage_gb" {
  description = "Maximum storage size (auto-scaling threshold)."
  type        = number
  default     = 100
}

variable "postgres_engine_version" {
  description = "Target Postgres engine version."
  type        = string
  default     = "17.4"
}

variable "publicly_accessible" {
  description = "Whether RDS assigns a public address for direct connections."
  type        = bool
  default     = false
}

variable "apply_immediately" {
  description = "Apply modifications immediately (may cause restarts) versus during the next maintenance window."
  type        = bool
  default     = true
}

variable "deletion_protection" {
  description = "Requires disabling before the DB can be destroyed."
  type        = bool
  default     = false
}

variable "skip_final_snapshot" {
  description = "Skip the final snapshot on destroy if no custom identifier is supplied."
  type        = bool
  default     = true
}

variable "final_snapshot_identifier" {
  description = "Optional name to use when creating a final snapshot on destroy."
  type        = string
  default     = ""
}
