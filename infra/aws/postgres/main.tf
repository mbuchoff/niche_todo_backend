# ABOUTME: Provisions the AWS networking, security, and RDS instance that hosts the Postgres test database.
# ABOUTME: Wraps the OpenTofu resources needed for a reproducible database using configurable credentials and access rules.

terraform {
  required_version = ">= 1.7.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.65.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
  profile = var.aws_profile
}

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  az_count = min(length(data.aws_availability_zones.available.names), 2)

  subnet_azs = {
    for idx, az in slice(data.aws_availability_zones.available.names, 0, local.az_count) : idx => az
  }
}

resource "aws_vpc" "todo_backend" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = {
    Name = "${var.stack_name}-vpc"
  }
}

resource "aws_internet_gateway" "todo_backend" {
  vpc_id = aws_vpc.todo_backend.id

  tags = {
    Name = "${var.stack_name}-igw"
  }
}

resource "aws_subnet" "public" {
  for_each = local.subnet_azs

  vpc_id                  = aws_vpc.todo_backend.id
  cidr_block              = cidrsubnet(var.vpc_cidr, 4, tonumber(each.key))
  availability_zone       = each.value
  map_public_ip_on_launch = true

  tags = {
    Name = "${var.stack_name}-public-${each.value}"
  }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.todo_backend.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.todo_backend.id
  }

  tags = {
    Name = "${var.stack_name}-public-rt"
  }
}

resource "aws_route_table_association" "public" {
  for_each = aws_subnet.public

  subnet_id      = each.value.id
  route_table_id = aws_route_table.public.id
}

resource "aws_security_group" "todo_backend_postgres" {
  name        = "${var.stack_name}-postgres"
  description = "Controls inbound connectivity for the Todo Backend Postgres database."
  vpc_id      = aws_vpc.todo_backend.id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.stack_name}-postgres"
  }
}

resource "aws_security_group_rule" "postgres_ingress" {
  for_each = toset(var.allowed_cidr_blocks)

  type              = "ingress"
  description       = "Allow Postgres from ${each.value}"
  from_port         = 5432
  to_port           = 5432
  protocol          = "tcp"
  cidr_blocks       = [each.value]
  security_group_id = aws_security_group.todo_backend_postgres.id
}

resource "aws_db_subnet_group" "todo_backend_postgres" {
  name       = "${var.stack_name}-postgres"
  subnet_ids = [for subnet in aws_subnet.public : subnet.id]

  tags = {
    Name = "${var.stack_name}-postgres"
  }
}

resource "aws_db_instance" "todo_backend_postgres" {
  identifier                 = "${var.stack_name}-postgres"
  allocated_storage          = var.db_allocated_storage_gb
  max_allocated_storage      = var.db_max_allocated_storage_gb
  storage_encrypted          = true
  auto_minor_version_upgrade = true
  backup_retention_period    = 0
  deletion_protection        = var.deletion_protection
  skip_final_snapshot        = var.final_snapshot_identifier == "" ? var.skip_final_snapshot : false
  final_snapshot_identifier  = var.final_snapshot_identifier == "" ? null : var.final_snapshot_identifier
  apply_immediately          = var.apply_immediately
  engine                     = "postgres"
  engine_version             = var.postgres_engine_version
  instance_class             = var.db_instance_class
  db_name                    = var.db_name
  username                   = var.db_username
  password                   = var.db_password
  publicly_accessible        = var.publicly_accessible
  port                       = 5432
  db_subnet_group_name       = aws_db_subnet_group.todo_backend_postgres.name
  vpc_security_group_ids     = [aws_security_group.todo_backend_postgres.id]

  tags = {
    Name = "${var.stack_name}-postgres"
  }
}
