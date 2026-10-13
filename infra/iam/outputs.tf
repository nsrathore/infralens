output "reader_role_arn" {
  value = aws_iam_role.reader.arn
}

output "operator_role_arn" {
  value = aws_iam_role.operator.arn
}

output "audit_table" {
  value = aws_dynamodb_table.audit.name
}

# Paste into ~/.aws/config:
#
# [profile infralens-reader]
# role_arn       = <reader_role_arn>
# source_profile = default
# region         = us-east-2
