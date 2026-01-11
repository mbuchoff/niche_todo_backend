# ABOUTME: Provisions the AWS network, ECS, and supporting resources that host the Todo Backend API.
# ABOUTME: Creates VPC networking, an ALB, Fargate service, IAM roles, and supporting secrets for deployments.

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
  region  = var.aws_region
  profile = var.aws_profile
}

data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_caller_identity" "current" {}

data "aws_partition" "current" {}

data "aws_cloudfront_cache_policy" "disabled" {
  name = "Managed-CachingDisabled"
}

data "aws_cloudfront_origin_request_policy" "all_viewer" {
  name = "Managed-AllViewer"
}

locals {
  az_count = min(length(data.aws_availability_zones.available.names), 2)
  subnet_azs = {
    for idx, az in slice(data.aws_availability_zones.available.names, 0, local.az_count) : idx => az
  }
  container_image = coalesce(var.container_image, "${aws_ecr_repository.todo_backend_api.repository_url}:latest")
}

resource "aws_vpc" "todo_backend_api" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = {
    Name = "${var.stack_name}-api-vpc"
  }
}

resource "aws_internet_gateway" "todo_backend_api" {
  vpc_id = aws_vpc.todo_backend_api.id

  tags = {
    Name = "${var.stack_name}-api-igw"
  }
}

resource "aws_subnet" "public" {
  for_each = local.subnet_azs

  vpc_id                  = aws_vpc.todo_backend_api.id
  cidr_block              = cidrsubnet(var.vpc_cidr, 4, tonumber(each.key))
  availability_zone       = each.value
  map_public_ip_on_launch = true

  tags = {
    Name = "${var.stack_name}-api-public-${each.value}"
  }
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.todo_backend_api.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.todo_backend_api.id
  }

  tags = {
    Name = "${var.stack_name}-api-public-rt"
  }
}

resource "aws_route_table_association" "public" {
  for_each       = aws_subnet.public
  subnet_id      = each.value.id
  route_table_id = aws_route_table.public.id
}

resource "aws_security_group" "alb" {
  name        = "${var.stack_name}-api-alb"
  description = "Ingress control for the Todo Backend public load balancer."
  vpc_id      = aws_vpc.todo_backend_api.id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.stack_name}-api-alb"
  }
}

resource "aws_security_group_rule" "alb_ingress" {
  for_each          = toset(var.allowed_ingress_cidr_blocks)
  type              = "ingress"
  description       = "Allow HTTP traffic from ${each.value}"
  from_port         = var.listener_port
  to_port           = var.listener_port
  protocol          = "tcp"
  cidr_blocks       = [each.value]
  security_group_id = aws_security_group.alb.id
}

resource "aws_security_group" "service" {
  name        = "${var.stack_name}-api-service"
  description = "Constrains inbound traffic to ECS tasks from the ALB."
  vpc_id      = aws_vpc.todo_backend_api.id

  ingress {
    description     = "Traffic from ALB"
    from_port       = var.container_port
    to_port         = var.container_port
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.stack_name}-api-service"
  }
}

resource "aws_security_group_rule" "db_ingress_from_service" {
  count                    = var.database_security_group_id == null ? 0 : 1
  type                     = "ingress"
  description              = "Allow Postgres from ECS service tasks."
  from_port                = var.database_port
  to_port                  = var.database_port
  protocol                 = "tcp"
  security_group_id        = var.database_security_group_id
  source_security_group_id = aws_security_group.service.id
}

resource "aws_lb" "todo_backend_api" {
  name               = "${var.stack_name}-api"
  internal           = false
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb.id]
  subnets            = [for subnet in aws_subnet.public : subnet.id]

  tags = {
    Name = "${var.stack_name}-api"
  }
}

resource "aws_lb_target_group" "todo_backend_api" {
  name        = "${var.stack_name}-api"
  port        = var.container_port
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = aws_vpc.todo_backend_api.id

  health_check {
    enabled             = true
    healthy_threshold   = 2
    unhealthy_threshold = 3
    interval            = 30
    timeout             = 5
    protocol            = "HTTP"
    matcher             = "200"
    path                = var.health_check_path
  }
}

resource "aws_lb_listener" "todo_backend_api" {
  load_balancer_arn = aws_lb.todo_backend_api.arn
  port              = var.listener_port
  protocol          = "HTTP"

  default_action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.todo_backend_api.arn
  }
}

resource "aws_cloudfront_distribution" "todo_backend_api" {
  enabled = true

  origin {
    domain_name = aws_lb.todo_backend_api.dns_name
    origin_id   = "${var.stack_name}-api-alb"

    custom_origin_config {
      http_port              = var.listener_port
      https_port             = 443
      origin_protocol_policy = "http-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  default_cache_behavior {
    target_origin_id       = "${var.stack_name}-api-alb"
    viewer_protocol_policy = "redirect-to-https"
    allowed_methods        = ["GET", "HEAD", "OPTIONS", "PUT", "PATCH", "POST", "DELETE"]
    cached_methods         = ["GET", "HEAD", "OPTIONS"]
    compress               = true
    cache_policy_id        = data.aws_cloudfront_cache_policy.disabled.id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer.id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    cloudfront_default_certificate = true
  }
}

resource "aws_cloudwatch_log_group" "todo_backend_api" {
  name              = "/todo-backend/${var.stack_name}/api"
  retention_in_days = var.log_retention_days
}

resource "aws_ssm_parameter" "todo_backend_db_connection" {
  name  = "/todo-backend/${var.stack_name}/db-connection"
  type  = "SecureString"
  value = var.database_connection_string
}

resource "aws_ecr_repository" "todo_backend_api" {
  name                 = var.ecr_repository_name
  image_tag_mutability = "MUTABLE"
  force_delete         = true

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_iam_role" "todo_backend_api_execution" {
  name = "${var.stack_name}-api-execution"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
        Action = "sts:AssumeRole"
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "execution_policy" {
  role       = aws_iam_role.todo_backend_api_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

resource "aws_iam_role_policy" "execution_parameter_access" {
  name = "${var.stack_name}-api-execution-ssm"
  role = aws_iam_role.todo_backend_api_execution.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "ssm:GetParameter",
          "ssm:GetParameters",
          "ssm:GetParametersByPath"
        ]
        Resource = [
          aws_ssm_parameter.todo_backend_db_connection.arn
        ]
      },
      {
        Effect = "Allow"
        Action = "kms:Decrypt"
        Resource = "arn:${data.aws_partition.current.partition}:kms:${var.aws_region}:${data.aws_caller_identity.current.account_id}:alias/aws/ssm"
      }
    ]
  })
}

resource "aws_iam_role" "todo_backend_api_task" {
  name = "${var.stack_name}-api-task"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Principal = {
          Service = "ecs-tasks.amazonaws.com"
        }
        Action = "sts:AssumeRole"
      }
    ]
  })
}

resource "aws_iam_role_policy" "task_parameter_access" {
  name = "${var.stack_name}-api-ssm"
  role = aws_iam_role.todo_backend_api_task.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "ssm:GetParameter",
          "ssm:GetParameters",
          "ssm:GetParametersByPath"
        ]
        Resource = [
          aws_ssm_parameter.todo_backend_db_connection.arn
        ]
      },
      {
        Effect = "Allow"
        Action = "kms:Decrypt"
        Resource = "arn:${data.aws_partition.current.partition}:kms:${var.aws_region}:${data.aws_caller_identity.current.account_id}:alias/aws/ssm"
      }
    ]
  })
}

resource "aws_ecs_cluster" "todo_backend_api" {
  name = "${var.stack_name}-api"
}

resource "aws_ecs_task_definition" "todo_backend_api" {
  family                   = "${var.stack_name}-api"
  cpu                      = var.task_cpu
  memory                   = var.task_memory
  network_mode             = "awsvpc"
  requires_compatibilities = ["FARGATE"]
  execution_role_arn       = aws_iam_role.todo_backend_api_execution.arn
  task_role_arn            = aws_iam_role.todo_backend_api_task.arn

  container_definitions = jsonencode([
    {
      name      = "todo-backend-api"
      image     = local.container_image
      essential = true
      portMappings = [
        {
          containerPort = var.container_port
          hostPort      = var.container_port
          protocol      = "tcp"
        }
      ]
      environment = [
        {
          name  = "ASPNETCORE_ENVIRONMENT"
          value = "Production"
        },
        {
          name  = "ASPNETCORE_URLS"
          value = "http://+:${var.container_port}"
        }
      ]
      secrets = [
        {
          name      = "ConnectionStrings__Database"
          valueFrom = aws_ssm_parameter.todo_backend_db_connection.arn
        }
      ]
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          awslogs-group         = aws_cloudwatch_log_group.todo_backend_api.name
          awslogs-region        = var.aws_region
          awslogs-stream-prefix = "todo-backend"
        }
      }
    }
  ])
}

resource "aws_ecs_service" "todo_backend_api" {
  name            = "${var.stack_name}-api"
  cluster         = aws_ecs_cluster.todo_backend_api.id
  task_definition = aws_ecs_task_definition.todo_backend_api.arn
  desired_count   = var.desired_count
  launch_type     = "FARGATE"
  platform_version = "LATEST"

  network_configuration {
    subnets          = [for subnet in aws_subnet.public : subnet.id]
    security_groups  = [aws_security_group.service.id]
    assign_public_ip = true
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.todo_backend_api.arn
    container_name   = "todo-backend-api"
    container_port   = var.container_port
  }

  health_check_grace_period_seconds = var.health_check_grace_period_seconds

  depends_on = [
    aws_lb_listener.todo_backend_api
  ]
}
