resource "random_password" "db_owner" {
  length  = 24
  special = false
}

resource "random_password" "db_app" {
  length  = 24
  special = false
}

resource "random_password" "rabbitmq" {
  length  = 24
  special = false
}

# Full connection strings — ECS secrets inject a single value per env var,
# and .NET connection strings can't be assembled from separate fragments.
resource "aws_secretsmanager_secret" "app_conn" {
  name                    = "seamline/app-connection-string"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "app_conn" {
  secret_id     = aws_secretsmanager_secret.app_conn.id
  secret_string = "Host=${aws_db_instance.postgres.address};Database=${var.db_name};Username=seamline_app;Password=${random_password.db_app.result}"
}

resource "aws_secretsmanager_secret" "migrator_conn" {
  name                    = "seamline/migrator-connection-string"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "migrator_conn" {
  secret_id     = aws_secretsmanager_secret.migrator_conn.id
  secret_string = "Host=${aws_db_instance.postgres.address};Database=${var.db_name};Username=${var.db_username};Password=${random_password.db_owner.result}"
}

resource "aws_secretsmanager_secret" "rabbitmq_password" {
  name                    = "seamline/rabbitmq-password"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "rabbitmq_password" {
  secret_id     = aws_secretsmanager_secret.rabbitmq_password.id
  secret_string = random_password.rabbitmq.result
}
