# ABOUTME: Configures OpenTofu state storage in an AWS S3 backend.
# ABOUTME: Backend settings are supplied via `tofu init -backend-config=...` to avoid hardcoding account-specific names.

terraform {
  backend "s3" {}
}

