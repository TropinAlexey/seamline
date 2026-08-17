resource "aws_db_subnet_group" "main" {
  name       = "seamline"
  subnet_ids = aws_subnet.private[*].id

  tags = { Name = "seamline" }
}

resource "aws_db_instance" "postgres" {
  identifier = "seamline"

  engine         = "postgres"
  engine_version = "17.5"
  instance_class = "db.t4g.micro"

  allocated_storage = 20
  storage_type      = "gp3"
  storage_encrypted = true

  db_name  = var.db_name
  username = var.db_username
  password = random_password.db_owner.result

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.rds.id]

  multi_az            = false
  publicly_accessible = false
  skip_final_snapshot = true

  tags = { Name = "seamline" }
}
