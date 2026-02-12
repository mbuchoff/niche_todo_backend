#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/../.." # repo root (todo-backend/backend)

require_file_contains() {
  local path="$1"
  local pattern="$2"

  if [[ ! -f "$path" ]]; then
    echo "Expected file to exist: $path" >&2
    exit 1
  fi

  if ! rg -n --fixed-strings "$pattern" "$path" >/dev/null; then
    echo "Expected '$path' to contain: $pattern" >&2
    exit 1
  fi
}

require_file_contains "infra/aws/app/backend.tf" 'backend "s3"'
require_file_contains "infra/aws/postgres/backend.tf" 'backend "s3"'
require_file_contains "infra/aws/app/variables.tf" "postgres_state_s3_bucket"
require_file_contains "infra/aws/app/variables.tf" "postgres_state_s3_key"
require_file_contains "infra/aws/app/main.tf" 'data "terraform_remote_state" "postgres_s3"'
require_file_contains "infra/aws/app/main.tf" 'backend = "s3"'

echo "OK: S3 backend and postgres remote_state wiring detected."

