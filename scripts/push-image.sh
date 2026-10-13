#!/usr/bin/env bash
# Build the sandbox app image and push it to ECR (repo created by infra/bootstrap).
# Usage: scripts/push-image.sh [tag] [platform]
set -euo pipefail
repo_root="$(cd "$(dirname "$0")/.." && pwd)"

tag="${1:-latest}"
platform="${2:-linux/amd64}"   # must match var.cpu_architecture (X86_64 -> linux/amd64, ARM64 -> linux/arm64)
region="${AWS_REGION:-us-east-2}"
account="$(aws sts get-caller-identity --query Account --output text)"
registry="${account}.dkr.ecr.${region}.amazonaws.com"
image="${registry}/infralens-sandbox:${tag}"

aws ecr get-login-password --region "$region" | docker login --username AWS --password-stdin "$registry"
docker buildx build --platform "$platform" -f "$repo_root/sandbox/app/Dockerfile" -t "$image" --push "$repo_root"
echo "Pushed $image"
