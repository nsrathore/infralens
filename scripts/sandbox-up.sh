#!/usr/bin/env bash
# Session start: push the app image, create the sandbox, wait for a healthy service (~5 min).
# Session end:   scripts/sandbox-down.sh   (do not skip it; the ALB alone is ~$0.55/day)
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
tf="terraform -chdir=$repo_root/infra/sandbox"
region="${AWS_REGION:-us-east-2}"
account="$(aws sts get-caller-identity --query Account --output text)"

"$repo_root/scripts/push-image.sh" latest

$tf init -input=false -backend-config="bucket=infralens-tfstate-${account}" >/dev/null
$tf apply -auto-approve -input=false

cluster="$($tf output -raw cluster)"
service="$($tf output -raw service)"
echo "Waiting for $service to become stable..."
aws ecs wait services-stable --region "$region" --cluster "$cluster" --services "$service"

echo "Sandbox up: $($tf output -raw alb_url)"
