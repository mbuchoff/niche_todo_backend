ABOUTME: Field guide for provisioning an AWS Postgres database for Todo Backend testing via OpenTofu.
ABOUTME: Documents required variables, execution steps, and teardown commands to keep the stack reproducible.

# AWS Postgres Stack

This stack builds the minimum AWS infrastructure needed for a disposable Postgres database that mirrors production behavior:

- Dedicated VPC with two public subnets (one per AZ) and routing through an internet gateway.
- Security group that only opens TCP 5432 to CIDR blocks you specify.
- Single-AZ Amazon RDS Postgres instance (defaults to Postgres 15 on `db.t4g.micro` hardware).

## Prerequisites

- [OpenTofu](https://opentofu.org/docs/cli/) (`tofu`) 1.7+ installed locally.
- AWS credentials exported in your shell (`AWS_PROFILE`, `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`, or SSO).
- A CIDR block representing where you will connect from (ex: `["203.0.113.42/32"]` for your laptop IP).

## Configure variables

1. Copy `postgres.auto.tfvars.example` to `postgres.auto.tfvars`.
2. Edit the copy with your values:
   - `db_password` must be at least 8 characters and satisfy AWS password rules.
   - `allowed_cidr_blocks` **must** be set to specific IP ranges; leaving it empty blocks all inbound traffic.
   - Set `publicly_accessible = true` when you need a public endpoint directly reachable from your laptop.
   - `aws_profile` defaults to `"niche-todo-admin"`; change it if your local `~/.aws/credentials` uses a different profile name.

Alternatively you can supply values with `-var` or a custom `-var-file`.

## Deploy the stack

```bash
cd infra/aws/postgres
cp backend.hcl.example backend.hcl
tofu init -backend-config=backend.hcl
tofu plan -var-file=postgres.auto.tfvars
tofu apply -var-file=postgres.auto.tfvars
```

If you already have local state (`terraform.tfstate`) and want to move it into S3, run:

```bash
cd infra/aws/postgres
cp backend.hcl.example backend.hcl
tofu init -backend-config=backend.hcl -migrate-state
```

After a successful apply, capture the outputs:

```bash
tofu output
tofu output db_connection_string
```

The connection string output is marked sensitive, so use `tofu output db_connection_string` when you need it.

## Connect to Postgres

The stack configures SSL and exposes the RDS endpoint directly. Example using `psql`:

```bash
psql "postgresql://<db_username>:<db_password>@$(tofu output -raw db_endpoint):5432/<db_name>?sslmode=require"
```

Update your `.NET` `appsettings.json` or user secrets with the same endpoint, username, and password. The Todo Backend API uses standard Npgsql connection strings, so reuse the `db_connection_string` output as-is.

## Teardown

When finished testing, destroy the stack to avoid ongoing costs:

```bash
cd infra/aws/postgres
tofu destroy -var-file=postgres.auto.tfvars
```

- Ensure `deletion_protection` is `false` before running destroy.
- If you want a snapshot, set `final_snapshot_identifier` to a unique name and `skip_final_snapshot = false`.

## Troubleshooting

- **No ingress from laptop:** Double-check `allowed_cidr_blocks` includes your current public IP and that `publicly_accessible` is enabled.
- **Apply blocked on password:** AWS enforces password complexity—use at least 8 chars with uppercase, lowercase, numbers, and symbols.
- **Need different size:** Override `db_instance_class` (for example `db.t4g.small`) in your tfvars file.
