resource "aws_ecs_cluster" "main" {
  name = "seamline"
}

resource "aws_cloudwatch_log_group" "ecs" {
  name              = "/ecs/seamline"
  retention_in_days = 7
}

# --- Service discovery (internal DNS for rabbitmq / acer-stub) ---

resource "aws_service_discovery_private_dns_namespace" "main" {
  name = "seamline.local"
  vpc  = aws_vpc.main.id
}

resource "aws_service_discovery_service" "rabbitmq" {
  name = "rabbitmq"

  dns_config {
    namespace_id = aws_service_discovery_private_dns_namespace.main.id
    dns_records {
      type = "A"
      ttl  = 10
    }
  }

  health_check_custom_config {
    failure_threshold = 1
  }
}

resource "aws_service_discovery_service" "acer_stub" {
  name = "acer-stub"

  dns_config {
    namespace_id = aws_service_discovery_private_dns_namespace.main.id
    dns_records {
      type = "A"
      ttl  = 10
    }
  }

  health_check_custom_config {
    failure_threshold = 1
  }
}

locals {
  account_id = data.aws_caller_identity.current.account_id
  ecr_prefix = "${local.account_id}.dkr.ecr.${var.aws_region}.amazonaws.com"

  log_config = {
    logDriver = "awslogs"
    options = {
      "awslogs-group"         = aws_cloudwatch_log_group.ecs.name
      "awslogs-region"        = var.aws_region
      "awslogs-stream-prefix" = "ecs"
    }
  }
}

# --- RabbitMQ ---

resource "aws_ecs_task_definition" "rabbitmq" {
  family                   = "seamline-rabbitmq"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_execution.arn

  container_definitions = jsonencode([{
    name         = "rabbitmq"
    image        = "${local.ecr_prefix}/seamline-rabbitmq:latest"
    essential    = true
    portMappings = [{ containerPort = 5672, protocol = "tcp" }]
    environment = [
      { name = "RABBITMQ_DEFAULT_USER", value = "seamline" },
    ]
    secrets = [
      { name = "RABBITMQ_DEFAULT_PASS", valueFrom = aws_secretsmanager_secret.rabbitmq_password.arn },
    ]
    logConfiguration = local.log_config
  }])
}

resource "aws_ecs_service" "rabbitmq" {
  name            = "rabbitmq"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.rabbitmq.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets         = aws_subnet.private[*].id
    security_groups = [aws_security_group.rabbitmq.id]
  }

  service_registries {
    registry_arn = aws_service_discovery_service.rabbitmq.arn
  }
}

# --- ACER stub ---

resource "aws_ecs_task_definition" "acer_stub" {
  family                   = "seamline-acer-stub"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_execution.arn

  container_definitions = jsonencode([{
    name             = "acer-stub"
    image            = "${local.ecr_prefix}/seamline-acer-stub:latest"
    essential        = true
    portMappings     = [{ containerPort = 8080, protocol = "tcp" }]
    logConfiguration = local.log_config
  }])
}

resource "aws_ecs_service" "acer_stub" {
  name            = "acer-stub"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.acer_stub.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets         = aws_subnet.private[*].id
    security_groups = [aws_security_group.ecs.id]
  }

  service_registries {
    registry_arn = aws_service_discovery_service.acer_stub.arn
  }
}

# --- API (the only service that runs migrations → gets migrator conn) ---

resource "aws_ecs_task_definition" "api" {
  family                   = "seamline-api"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "512"
  memory                   = "1024"
  execution_role_arn       = aws_iam_role.ecs_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  container_definitions = jsonencode([{
    name         = "api"
    image        = "${local.ecr_prefix}/seamline-api:${var.api_image_tag}"
    essential    = true
    portMappings = [{ containerPort = 8080, protocol = "tcp" }]
    environment = [
      { name = "MessageBroker__Transport", value = "RabbitMq" },
      { name = "MessageBroker__RabbitMq__Host", value = "rabbitmq.seamline.local" },
      { name = "MessageBroker__RabbitMq__Username", value = "seamline" },
    ]
    secrets = [
      { name = "ConnectionStrings__Postgres", valueFrom = aws_secretsmanager_secret.app_conn.arn },
      { name = "ConnectionStrings__PostgresMigrator", valueFrom = aws_secretsmanager_secret.migrator_conn.arn },
      { name = "MessageBroker__RabbitMq__Password", valueFrom = aws_secretsmanager_secret.rabbitmq_password.arn },
    ]
    logConfiguration = local.log_config
  }])
}

resource "aws_ecs_service" "api" {
  name            = "api"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.api.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets         = aws_subnet.private[*].id
    security_groups = [aws_security_group.ecs.id]
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.api.arn
    container_name   = "api"
    container_port   = 8080
  }

  depends_on = [aws_lb_listener.api]
}

# --- Valuation Worker (no migrations, no migrator conn) ---

resource "aws_ecs_task_definition" "valuation_worker" {
  family                   = "seamline-valuation-worker"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  container_definitions = jsonencode([{
    name      = "valuation-worker"
    image     = "${local.ecr_prefix}/seamline-valuation-worker:${var.valuation_worker_image_tag}"
    essential = true
    secrets = [
      { name = "ConnectionStrings__Postgres", valueFrom = aws_secretsmanager_secret.app_conn.arn },
    ]
    logConfiguration = local.log_config
  }])
}

resource "aws_ecs_service" "valuation_worker" {
  name            = "valuation-worker"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.valuation_worker.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets         = aws_subnet.private[*].id
    security_groups = [aws_security_group.ecs.id]
  }
}

# --- Reporting Worker (no migrations, no migrator conn) ---

resource "aws_ecs_task_definition" "reporting_worker" {
  family                   = "seamline-reporting-worker"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = "256"
  memory                   = "512"
  execution_role_arn       = aws_iam_role.ecs_execution.arn
  task_role_arn            = aws_iam_role.ecs_task.arn

  container_definitions = jsonencode([{
    name      = "reporting-worker"
    image     = "${local.ecr_prefix}/seamline-reporting-worker:${var.reporting_worker_image_tag}"
    essential = true
    environment = [
      { name = "AcerStub__BaseUrl", value = "http://acer-stub.seamline.local:8080" },
    ]
    secrets = [
      { name = "ConnectionStrings__Postgres", valueFrom = aws_secretsmanager_secret.app_conn.arn },
    ]
    logConfiguration = local.log_config
  }])
}

resource "aws_ecs_service" "reporting_worker" {
  name            = "reporting-worker"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.reporting_worker.arn
  desired_count   = 1
  launch_type     = "FARGATE"

  network_configuration {
    subnets         = aws_subnet.private[*].id
    security_groups = [aws_security_group.ecs.id]
  }
}
