#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"
terraform -chdir="$repo_root/infra/sandbox" destroy -auto-approve -input=false
echo "Sandbox destroyed. Bootstrap (state bucket, budget) and IAM roles are kept."
