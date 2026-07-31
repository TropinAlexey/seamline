variable "aws_region" {
  type    = string
  default = "eu-central-1"
}

variable "db_username" {
  type    = string
  default = "seamline"
}

variable "db_name" {
  type    = string
  default = "seamline"
}

variable "api_image_tag" {
  type    = string
  default = "latest"
}

variable "valuation_worker_image_tag" {
  type    = string
  default = "latest"
}

variable "reporting_worker_image_tag" {
  type    = string
  default = "latest"
}
