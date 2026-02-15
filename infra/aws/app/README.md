ABOUTME: Field guide for provisioning the AWS Todo Backend API hosting stack with OpenTofu.
ABOUTME: Details prerequisites, configuration variables, and deployment steps for ECS + ALB infrastructure.

# AWS Todo Backend API Stack

This stack creates the AWS infrastructure that serves the Todo Backend API:

- Dedicated VPC, public subnets, and routing to the internet.
- Application Load Balancer with security groups that only allow approved CIDR blocks.
- Fargate-based ECS cluster, task definition, and service.
- Amazon ECR repository for container images.
- CloudWatch log group plus IAM roles/policies for task execution.
- AWS Systems Manager parameter that stores the Postgres connection string (consumed by ECS as a secret).

## Prerequisites

- [OpenTofu](https://opentofu.org/docs/cli/) (`tofu`) 1.7+ installed locally.
- AWS credentials configured via profile, env vars, or SSO.
- A Postgres connection string that the API can use.
- Docker image builds available (see `src/TodoBackend.Api/Dockerfile` and the GitHub Actions workflow).

## Configure variables

All runtime knobs live in `variables.tf`. Recommended workflow:

1. Copy `app.auto.tfvars.example` to `app.auto.tfvars` (ignored by git).
2. Update the copy with environment-specific values. At minimum set `database_connection_string`.
3. Alternatively pass values via `-var-file` or `TF_VAR_*` environment variables (the GitHub workflow uses secrets for this).

Optional: reuse the Postgres stack VPC by setting `postgres_state_path` to the Postgres stack `terraform.tfstate` (or set `existing_vpc_id` and `public_subnet_ids` directly).

If you want Swagger enabled on the deployed API, set `aspnetcore_environment = "Development"` in your tfvars (the default is `"Production"`).
Set `google_client_id` to the Google OAuth **Web client ID** used by the Android app.

## Deploy

```bash
cd infra/aws/app
cp backend.hcl.example backend.hcl
tofu init -backend-config=backend.hcl
tofu plan -var-file=app.auto.tfvars
tofu apply -var-file=app.auto.tfvars
```

If you already have local state (`terraform.tfstate`) and want to move it into S3, run:

```bash
cd infra/aws/app
cp backend.hcl.example backend.hcl
tofu init -backend-config=backend.hcl -migrate-state
```

The apply output includes the ALB DNS name plus the ECR repository URL. After pushing a container image, run `tofu apply` again with `-var 'container_image=<image-uri>'` so the ECS service updates to the latest digest.

## Destroy

```bash
cd infra/aws/app
tofu destroy -var-file=app.auto.tfvars
```

- Wait for the ECS service to drain before destroy completes.
- Delete leftover load balancer ENIs in case AWS reports dependency violations.
- The ECR repository is force-deleted, but deregister task definitions manually if you want a clean slate.

## GitHub Actions integration

- `.github/workflows/deploy.yml` builds and pushes the Docker image, then runs `tofu apply` inside this directory.
- Configure these repository secrets:
  - `AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY` (or an IAM role/identity provider) for authentication.
  - `AWS_REGION` if you override the default region.
  - `DATABASE_CONNECTION_STRING` for the Postgres endpoint.
  - `GOOGLE_CLIENT_ID` for the OAuth web client ID.
- Optional: `TF_VAR_allowed_ingress_cidr_blocks` to restrict ALB exposure without editing tfvars.

Update the workflow env variables if you change repository names or paths. Make sure `TF_VAR_container_image` always points to the image you just pushed so the ECS service picks up the new revision.
