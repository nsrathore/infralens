output "state_bucket" {
  value = aws_s3_bucket.state.bucket
}

output "ecr_repository_url" {
  value = aws_ecr_repository.sandbox.repository_url
}
