output "alb_dns" {
  value       = aws_lb.api.dns_name
  description = "Public DNS of the API load balancer"
}

output "ecr_api_url" {
  value = aws_ecr_repository.repos["seamline-api"].repository_url
}

output "ecr_valuation_worker_url" {
  value = aws_ecr_repository.repos["seamline-valuation-worker"].repository_url
}

output "ecr_reporting_worker_url" {
  value = aws_ecr_repository.repos["seamline-reporting-worker"].repository_url
}

output "ecr_acer_stub_url" {
  value = aws_ecr_repository.repos["seamline-acer-stub"].repository_url
}

output "ecr_rabbitmq_url" {
  value = aws_ecr_repository.repos["seamline-rabbitmq"].repository_url
}

output "rds_endpoint" {
  value = aws_db_instance.postgres.endpoint
}
